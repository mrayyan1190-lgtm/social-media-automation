using System.Text.Json;
using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.AiGeneration.Contracts;
using SocialMediaAutomation.Application.AiGeneration.DTOs;
using SocialMediaAutomation.Application.Common;
using SocialMediaAutomation.Application.Common.Authorization;
using SocialMediaAutomation.Domain.Common;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Application.AiGeneration.Services;

/// <summary>
/// AI Studio input. Brand description, audience and tone come from the brand profile; <see cref="Tone"/> optionally
/// overrides the tone.
/// </summary>
public sealed record RequestGenerationCommand(
    Guid OrganizationId,
    string? Prompt,
    IReadOnlyCollection<SocialPlatform>? Platforms,
    BrandTone? Tone,
    ContentLength Length = ContentLength.MEDIUM,
    HashtagMode Hashtags = HashtagMode.AUTO,
    EmojiMode Emojis = EmojiMode.AUTO,
    IReadOnlyList<Guid>? MediaAssetIds = null,
    string? VisionModel = null,
    bool WithImage = false,
    string? ImageModel = null,
    bool Unified = false,
    int ImageCount = 1);

/// <summary>
/// API side of AI work: validates access and inputs, freezes an input snapshot and enqueues a durable request.
/// It never calls the LLM; the AI worker does, after the HTTP request has returned.
/// </summary>
public sealed class AiGenerationService(
    OrganizationAuthorizer authorizer,
    ICorrelationContext correlation,
    IAiRepository ai,
    AiConnectionResolver llmConnections,
    IBrandProfileRepository brandProfiles,
    IOrganizationRepository organizations,
    IPostRepository posts,
    IMediaRepository media,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public const int PromptMax = 2000;

    /// <summary>
    /// Enqueues prompt-based generation: the worker writes one new draft post per platform. A repeated click with the
    /// same input while that generation is still running returns it instead of starting another.
    /// </summary>
    public async Task<AiGenerationRequestDto> RequestGenerationAsync(RequestGenerationCommand command, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(command.OrganizationId, Permission.ManageContent, cancellationToken);
        var prompt = Guard.Required(command.Prompt, "Prompt", PromptMax);
        var platforms = (command.Platforms ?? []).Select(p => Guard.Defined(p, "Platforms")).Distinct().Order().ToList();
        if (platforms.Count == 0)
            throw Guard.Invalid("Platforms", "must include at least one of LinkedIn, Facebook, Instagram");

        // Uploaded images are read by a Hugging Face vision model; otherwise the platform LLM writes the text.
        // Text + Image: an uploaded image is attached as it is; without uploads the LLM plans an image that an image model renders.
        var vision = await VisionAsync(access, command.MediaAssetIds, platforms, command.WithImage, cancellationToken);
        var imageCount = ImageCount(command.ImageCount);
        ImagePlanSnapshot? imagePlan = null;
        if (vision is null && command.WithImage)
        {
            var image = await llmConnections.ResolveImageAsync(cancellationToken)
                ?? throw new BusinessException(ErrorCodes.AiNotConfigured, "AI image generation is not available yet. Choose Text, or contact support.");
            imagePlan = new ImagePlanSnapshot(image.Provider,
                image.Provider == AiProvider.HuggingFace
                    ? ChooseModel(command.ImageModel, AiModelCatalog.WithDefault(AiModelCatalog.HuggingFaceImageModels, image.Model), "Image model")
                    : image.Model,
                image.Size, Count: imageCount > 1 ? imageCount : null);
        }
        else if (vision is null)
        {
            imagePlan = await OnRequestImagePlanAsync(cancellationToken);
        }
        var visionModels = vision is null ? [] : await llmConnections.VisionModelsAsync(cancellationToken);
        var llm = vision is null
            ? await RequireLlmAsync(access, cancellationToken)
            : visionModels.Count > 0
                && await llmConnections.ResolveHuggingFaceAsync(ChooseModel(command.VisionModel, visionModels, "Vision model"), cancellationToken) is { } visionLlm
                ? visionLlm
                : throw new BusinessException(ErrorCodes.AiNotConfigured, "Image understanding is not available right now. Remove the images, or contact support.");
        var brief = await BriefAsync(access, prompt, command.Tone is { } tone ? Guard.Defined(tone, "Tone") : null,
            Guard.Defined(command.Length, "Length"), Guard.Defined(command.Hashtags, "Hashtags"), Guard.Defined(command.Emojis, "Emojis"), cancellationToken);
        var snapshot = JsonSerializer.Serialize(await SnapshotAsync(access, brief, platforms, rewrite: null, cancellationToken) with
            {
                Vision = vision, ImagePlan = imagePlan,
                Unified = command.Unified && platforms.Count > 1 ? true : null,
            },
            AiVersions.Json);
        if (await ai.FindActiveGenerationAsync(access.OrganizationId, access.UserId, snapshot, cancellationToken) is { } active)
            return await ToDtoAsync(access, active, cancellationToken);

        var request = AiGenerationRequest.CreateGeneration(
            Guid.NewGuid(), access.OrganizationId, platforms, snapshot,
            AiVersions.GenerationPrompt, AiVersions.ContentSchema, llm.Provider, llm.Model, access.UserId, Now);
        request.AttachCorrelation(correlation.CorrelationId ?? CorrelationIds.New());
        ai.Add(request);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return AiGenerationRequestDto.From(request, []);
    }

    /// <summary>Enqueues a rewrite of the post's current version, which must be the version the user is looking at.</summary>
    public async Task<AiGenerationRequestDto> RequestRewriteAsync(
        Guid organizationId, Guid postId, RewriteAction action, string? customInstruction, byte[]? expectedRowVersion, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ManageContent, cancellationToken);
        var post = await LoadEditablePostAsync(access, postId, expectedRowVersion, "rewritten", cancellationToken);
        if (await ai.FindActiveRequestAsync(access.OrganizationId, AiRequestKind.Rewrite, post.Id, cancellationToken) is { } active)
            return await ToDtoAsync(access, active, cancellationToken);

        var llm = await RequireLlmAsync(access, cancellationToken);
        var source = await CurrentVersionAsync(access, post, cancellationToken);
        var rewrite = new RewriteSnapshot(post.Platform, action, customInstruction?.Trim(), SourceContent(source));
        // A custom instruction may ask for a new or changed image ("make the image brighter"); the model decides.
        var snapshot = await SnapshotAsync(access, await BriefAsync(access, null, null, cancellationToken: cancellationToken), [post.Platform], rewrite, cancellationToken)
            with { ImagePlan = action == RewriteAction.Custom ? await OnRequestImagePlanAsync(cancellationToken) : null };

        var request = AiGenerationRequest.CreateRewrite(
            Guid.NewGuid(), post, source.Id, action, customInstruction, JsonSerializer.Serialize(snapshot, AiVersions.Json),
            AiVersions.RewritePrompt, AiVersions.ContentSchema, llm.Provider, llm.Model, access.UserId, Now);
        request.AttachCorrelation(correlation.CorrelationId ?? CorrelationIds.New());
        ai.Add(request);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return AiGenerationRequestDto.From(request, []);
    }

    /// <summary>
    /// Enqueues an image for the post's current version (the one the user is looking at). The worker stores it as an
    /// AI-generated MediaAsset and attaches it as a new version, which needs review like any other change.
    /// </summary>
    public async Task<AiGenerationRequestDto> RequestImageAsync(
        Guid organizationId, Guid postId, string? instruction, byte[]? expectedRowVersion, CancellationToken cancellationToken, string? model = null,
        int count = 1)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ManageContent, cancellationToken);
        count = ImageCount(count);
        var post = await LoadEditablePostAsync(access, postId, expectedRowVersion, "given a new image", cancellationToken);
        if (await ai.FindActiveRequestAsync(access.OrganizationId, AiRequestKind.ImageGeneration, post.Id, cancellationToken) is { } active)
            return await ToDtoAsync(access, active, cancellationToken);

        var image = await llmConnections.ResolveImageAsync(cancellationToken)
            ?? throw new BusinessException(ErrorCodes.AiNotConfigured, "AI image generation is not available yet. Please contact support.");
        if (!string.IsNullOrWhiteSpace(model))
        {
            if (image.Provider != AiProvider.HuggingFace)
                throw Guard.Invalid("Image model", "can only be chosen when image generation uses Hugging Face");
            image = image with { Model = ChooseModel(model, AiModelCatalog.WithDefault(AiModelCatalog.HuggingFaceImageModels, image.Model), "Image model") };
        }
        var source = await CurrentVersionAsync(access, post, cancellationToken);
        var imageSnapshot = new ImageSnapshot(post.Platform, instruction?.Trim(), SourceContent(source), image.Size, count > 1 ? count : null);
        var snapshot = await SnapshotAsync(access, await BriefAsync(access, null, null, cancellationToken: cancellationToken), [post.Platform], null, cancellationToken)
            with { Image = imageSnapshot };

        var request = AiGenerationRequest.CreateImage(Guid.NewGuid(), post, source.Id, instruction,
            JsonSerializer.Serialize(snapshot, AiVersions.Json), AiVersions.ImagePrompt, image.Provider, image.Model, access.UserId, Now);
        request.AttachCorrelation(correlation.CorrelationId ?? CorrelationIds.New());
        ai.Add(request);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return AiGenerationRequestDto.From(request, []);
    }

    /// <summary>Which AI actions this workspace can use (so the UI shows only working buttons). Any member may ask.</summary>
    public async Task<AiCapabilitiesDto> CapabilitiesAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ViewContent, cancellationToken);
        var image = await llmConnections.ResolveImageAsync(cancellationToken);
        var visionModels = await llmConnections.VisionModelsAsync(cancellationToken);
        return new AiCapabilitiesDto(
            await llmConnections.ResolveAsync(access.OrganizationId, cancellationToken) is not null,
            image is not null,
            visionModels.Count > 0,
            image?.Provider == AiProvider.HuggingFace ? AiModelCatalog.WithDefault(AiModelCatalog.HuggingFaceImageModels, image.Model) : [],
            visionModels);
    }

    public async Task<AiGenerationRequestDto> GetAsync(Guid organizationId, Guid requestId, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ViewContent, cancellationToken);
        var request = await ai.GetRequestAsync(requestId, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.NotFound, "AI request not found.");
        access.EnsureOwns(request.OrganizationId, "AI request");
        return await ToDtoAsync(access, request, cancellationToken);
    }

    public async Task<IReadOnlyList<ContentEvaluationDto>> ListEvaluationsAsync(Guid organizationId, Guid postId, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ViewContent, cancellationToken);
        var post = await posts.GetAsync(postId, cancellationToken) ?? throw new BusinessException(ErrorCodes.NotFound, "Post not found.");
        access.EnsureOwns(post.OrganizationId, "Post");
        return await ai.ListEvaluationsAsync(access.OrganizationId, post.Id, cancellationToken);
    }

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    /// <summary>An image only if the user's words ask for one (configured default model); null when image generation is off.</summary>
    private async Task<ImagePlanSnapshot?> OnRequestImagePlanAsync(CancellationToken cancellationToken) =>
        await llmConnections.ResolveImageAsync(cancellationToken) is { } image
            ? new ImagePlanSnapshot(image.Provider, image.Model, image.Size, OnlyIfRequested: true)
            : null;

    /// <summary>How many images to generate: 1 to <see cref="PlatformRules.MaxImages"/>.</summary>
    private static int ImageCount(int count) =>
        count is >= 1 and <= PlatformRules.MaxImages ? count : throw Guard.Invalid("Image count", $"must be between 1 and {PlatformRules.MaxImages}");

    /// <summary>The chosen model when it is on the list (first entry when none was chosen).</summary>
    private static string ChooseModel(string? model, IReadOnlyList<string> allowed, string field) =>
        string.IsNullOrWhiteSpace(model) ? allowed[0]
        : allowed.Contains(model.Trim()) ? model.Trim()
        : throw Guard.Invalid(field, $"must be one of {string.Join(", ", allowed)}");

    /// <summary>
    /// Uploaded images for a vision model: this workspace's own assets, a few at most, small enough to send inline. When
    /// attached (Text + Image) every one must already suit every requested platform (e.g. JPEG for Instagram).
    /// </summary>
    private async Task<VisionSnapshot?> VisionAsync(
        OrganizationAccess access, IReadOnlyList<Guid>? mediaAssetIds, IReadOnlyList<SocialPlatform> platforms, bool attach, CancellationToken cancellationToken)
    {
        var ids = (mediaAssetIds ?? []).Distinct().ToList();
        if (ids.Count == 0)
            return null;
        if (ids.Count > AiModelCatalog.MaxVisionImages)
            throw Guard.Invalid("Images", $"at most {AiModelCatalog.MaxVisionImages} images can be analysed at once");
        long total = 0;
        foreach (var id in ids)
        {
            var asset = await media.GetAsync(id, cancellationToken) ?? throw new BusinessException(ErrorCodes.NotFound, "Image not found.");
            access.EnsureOwns(asset.OrganizationId, "Image");
            total += asset.SizeBytes;
            if (attach)
                platforms.ToList().ForEach(asset.EnsureSuitableFor);
        }
        if (total > AiModelCatalog.MaxVisionBytes)
            throw Guard.Invalid("Images", $"are too large to analyse together (max {AiModelCatalog.MaxVisionBytes / 1024 / 1024} MB in total); use fewer or smaller images");
        return new VisionSnapshot(ids, attach);
    }

    private async Task<Post> LoadEditablePostAsync(
        OrganizationAccess access, Guid postId, byte[]? expectedRowVersion, string what, CancellationToken cancellationToken)
    {
        var post = await posts.GetAsync(postId, cancellationToken) ?? throw new BusinessException(ErrorCodes.NotFound, "Post not found.");
        access.EnsureOwns(post.OrganizationId, "Post");
        ConcurrencyGuard.EnsureCurrent(post.RowVersion, expectedRowVersion);
        if (post.EditorialState is EditorialState.IN_REVIEW or EditorialState.ARCHIVED)
            throw new BusinessException(ErrorCodes.InvalidStateTransition, $"A post in {post.EditorialState} cannot be {what}.");
        return post;
    }

    private async Task<PostVersion> CurrentVersionAsync(OrganizationAccess access, Post post, CancellationToken cancellationToken) =>
        await posts.GetVersionAsync(access.OrganizationId, post.Id, post.CurrentVersionId!.Value, cancellationToken)
        ?? throw new InvalidOperationException($"Post {post.Id} has no current version row.");

    private static SourceContentSnapshot SourceContent(PostVersion source) =>
        new(source.VersionNumber, source.Hook, source.Body, source.CallToAction, source.Hashtags, source.Images.Count > 0);

    private async Task<LlmConnection> RequireLlmAsync(OrganizationAccess access, CancellationToken cancellationToken) =>
        await llmConnections.ResolveAsync(access.OrganizationId, cancellationToken)
        ?? throw new BusinessException(ErrorCodes.AiNotConfigured, "AI content generation is not available yet. Please contact support.");

    /// <summary>The request plus the brand's audience and tone, so the user never re-enters brand information.</summary>
    private async Task<BriefSnapshot> BriefAsync(
        OrganizationAccess access, string? prompt, BrandTone? tone, ContentLength length = ContentLength.MEDIUM,
        HashtagMode hashtags = HashtagMode.AUTO, EmojiMode emojis = EmojiMode.AUTO, CancellationToken cancellationToken = default)
    {
        var brand = await RequireBrandAsync(access, cancellationToken);
        return new BriefSnapshot(prompt, brand.TargetAudience, tone ?? brand.BrandTone, length, hashtags, emojis);
    }

    private async Task<BrandProfile> RequireBrandAsync(OrganizationAccess access, CancellationToken cancellationToken) =>
        await brandProfiles.GetByOrganizationAsync(access.OrganizationId, cancellationToken)
        ?? throw new BusinessException(ErrorCodes.ValidationFailed, "Complete the brand profile before generating content.");

    /// <summary>Freezes every input the model may see: resolved brand, the request and the target platforms.</summary>
    private async Task<AiInputSnapshot> SnapshotAsync(
        OrganizationAccess access, BriefSnapshot brief, IReadOnlyList<SocialPlatform> platforms, RewriteSnapshot? rewrite, CancellationToken cancellationToken)
    {
        var brand = await RequireBrandAsync(access, cancellationToken);
        var organization = await organizations.GetAsync(access.OrganizationId, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.NotFound, "Organization not found.");

        return new AiInputSnapshot(
            AiVersions.Snapshot,
            access.OrganizationId,
            organization.DefaultLanguage,
            new BrandSnapshot(organization.Name, organization.Industry, brand.BusinessDescription, brand.TargetAudience, brand.BrandTone,
                brand.ProhibitedTerms, brand.UpdatedAtUtc),
            brief,
            platforms.Order().ToList(),
            rewrite);
    }

    private async Task<AiGenerationRequestDto> ToDtoAsync(OrganizationAccess access, AiGenerationRequest request, CancellationToken cancellationToken) =>
        AiGenerationRequestDto.From(request, await ai.ListOutputsAsync(access.OrganizationId, request.Id, cancellationToken));
}
