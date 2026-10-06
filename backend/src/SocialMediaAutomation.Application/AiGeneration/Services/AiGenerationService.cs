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
/// overrides the tone. <see cref="CampaignId"/> only files the resulting drafts into a campaign folder.
/// </summary>
public sealed record RequestGenerationCommand(
    Guid OrganizationId,
    string? Prompt,
    IReadOnlyCollection<SocialPlatform>? Platforms,
    Guid? CampaignId,
    BrandTone? Tone,
    ContentLength Length = ContentLength.MEDIUM,
    HashtagMode Hashtags = HashtagMode.AUTO,
    EmojiMode Emojis = EmojiMode.AUTO);

/// <summary>
/// API side of AI work: validates access and inputs, freezes an input snapshot and enqueues a durable request.
/// It never calls the LLM; the AI worker does, after the HTTP request has returned.
/// </summary>
public sealed class AiGenerationService(
    OrganizationAuthorizer authorizer,
    ICorrelationContext correlation,
    IAiRepository ai,
    AiConnectionResolver llmConnections,
    ICampaignRepository campaigns,
    IBrandProfileRepository brandProfiles,
    IOrganizationRepository organizations,
    IPostRepository posts,
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
        var access = await authorizer.AuthorizeAsync(command.OrganizationId, Permission.ManageCampaigns, cancellationToken);
        var prompt = Guard.Required(command.Prompt, "Prompt", PromptMax);
        var platforms = (command.Platforms ?? []).Select(p => Guard.Defined(p, "Platforms")).Distinct().Order().ToList();
        if (platforms.Count == 0)
            throw Guard.Invalid("Platforms", "must include at least one of LinkedIn, Facebook, Instagram");
        Campaign? campaign = null;
        if (command.CampaignId is { } campaignId)
        {
            campaign = await campaigns.GetAsync(campaignId, cancellationToken) ?? throw new BusinessException(ErrorCodes.NotFound, "Campaign not found.");
            access.EnsureOwns(campaign.OrganizationId, "Campaign");
            campaign.EnsureEditable();
        }

        var llm = await RequireLlmAsync(access, cancellationToken);
        var brief = await BriefAsync(access, prompt, command.Tone is { } tone ? Guard.Defined(tone, "Tone") : null,
            Guard.Defined(command.Length, "Length"), Guard.Defined(command.Hashtags, "Hashtags"), Guard.Defined(command.Emojis, "Emojis"), cancellationToken);
        var snapshot = JsonSerializer.Serialize(await SnapshotAsync(access, brief, platforms, rewrite: null, cancellationToken), AiVersions.Json);
        if (await ai.FindActiveGenerationAsync(access.OrganizationId, access.UserId, campaign?.Id, snapshot, cancellationToken) is { } active)
            return await ToDtoAsync(access, active, cancellationToken);

        var request = AiGenerationRequest.CreateGeneration(
            Guid.NewGuid(), access.OrganizationId, campaign, platforms, snapshot,
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
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ManageCampaigns, cancellationToken);
        var post = await LoadEditablePostAsync(access, postId, expectedRowVersion, "rewritten", cancellationToken);
        if (await ai.FindActiveRequestAsync(access.OrganizationId, AiRequestKind.Rewrite, post.Id, cancellationToken) is { } active)
            return await ToDtoAsync(access, active, cancellationToken);

        var llm = await RequireLlmAsync(access, cancellationToken);
        var source = await CurrentVersionAsync(access, post, cancellationToken);
        var rewrite = new RewriteSnapshot(post.Platform, action, customInstruction?.Trim(), SourceContent(source));
        var snapshot = await SnapshotAsync(access, await BriefAsync(access, null, null, cancellationToken: cancellationToken), [post.Platform], rewrite, cancellationToken);

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
        Guid organizationId, Guid postId, string? instruction, byte[]? expectedRowVersion, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ManageCampaigns, cancellationToken);
        var post = await LoadEditablePostAsync(access, postId, expectedRowVersion, "given a new image", cancellationToken);
        if (await ai.FindActiveRequestAsync(access.OrganizationId, AiRequestKind.ImageGeneration, post.Id, cancellationToken) is { } active)
            return await ToDtoAsync(access, active, cancellationToken);

        var image = await llmConnections.ResolveImageAsync(cancellationToken)
            ?? throw new BusinessException(ErrorCodes.AiNotConfigured, "AI image generation is not available yet. Please contact support.");
        var source = await CurrentVersionAsync(access, post, cancellationToken);
        var imageSnapshot = new ImageSnapshot(post.Platform, instruction?.Trim(), SourceContent(source), image.Size);
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
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ViewCampaigns, cancellationToken);
        return new AiCapabilitiesDto(
            await llmConnections.ResolveAsync(access.OrganizationId, cancellationToken) is not null,
            await llmConnections.ResolveImageAsync(cancellationToken) is not null);
    }

    public async Task<AiGenerationRequestDto> GetAsync(Guid organizationId, Guid requestId, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ViewCampaigns, cancellationToken);
        var request = await ai.GetRequestAsync(requestId, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.NotFound, "AI request not found.");
        access.EnsureOwns(request.OrganizationId, "AI request");
        return await ToDtoAsync(access, request, cancellationToken);
    }

    public async Task<IReadOnlyList<ContentEvaluationDto>> ListEvaluationsAsync(Guid organizationId, Guid postId, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ViewCampaigns, cancellationToken);
        var post = await posts.GetAsync(postId, cancellationToken) ?? throw new BusinessException(ErrorCodes.NotFound, "Post not found.");
        access.EnsureOwns(post.OrganizationId, "Post");
        return await ai.ListEvaluationsAsync(access.OrganizationId, post.Id, cancellationToken);
    }

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

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
        new(source.VersionNumber, source.Hook, source.Body, source.CallToAction, source.Hashtags, source.MediaAssetId is not null);

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
