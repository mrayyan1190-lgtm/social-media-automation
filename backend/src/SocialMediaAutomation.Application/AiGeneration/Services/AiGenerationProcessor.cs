using System.Text.Json;
using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.AiGeneration.Contracts;
using SocialMediaAutomation.Application.Audit.Services;
using SocialMediaAutomation.Application.Media.Services;
using SocialMediaAutomation.Application.AiGeneration.Prompts;
using SocialMediaAutomation.Application.AiGeneration.Validation;
using SocialMediaAutomation.Application.Publications.Services;
using SocialMediaAutomation.Domain.Common;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Application.AiGeneration.Services;

/// <summary>
/// Worker side of AI work. Claims one durable request, calls the LLM with no database transaction open, validates
/// the output, and then persists versions, evaluations, outputs and call logs together with the final status in one
/// transaction that is fenced by the lease token. AI never approves, schedules or publishes anything here.
/// </summary>
public sealed class AiGenerationProcessor(
    IAiWorkQueue queue,
    IAiRepository ai,
    IPostRepository posts,
    IMediaRepository media,
    ISocialAccountRepository socialAccounts,
    ActivityRecorder activity,
    PublicationService publications,
    AiConnectionResolver llmConnections,
    ILlmClient llm,
    IImageGenerationProvider images,
    IMediaStorage mediaStorage,
    MediaService mediaService,
    ISystemEventLog systemLog,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);

    private readonly List<AiCallLog> callLogs = [];
    /// <summary>Technical reasons behind a customer warning (e.g. why an image was not made); they go to the admin System log only.</summary>
    private readonly List<string> diagnostics = [];

    /// <summary>Claims at most one due request (one short SQL statement); process it with <see cref="ProcessAsync"/>.</summary>
    public Task<(Guid RequestId, Guid LeaseToken)?> ClaimNextAsync(string workerId, CancellationToken cancellationToken) =>
        queue.TryClaimNextAsync(workerId, LeaseDuration, cancellationToken);

    /// <summary>Processes at most one due request. Returns false when nothing was due.</summary>
    public async Task<bool> ProcessNextAsync(string workerId, CancellationToken cancellationToken)
    {
        if (await queue.TryClaimNextAsync(workerId, LeaseDuration, cancellationToken) is not { } claim)
            return false;
        await ProcessAsync(claim.RequestId, claim.LeaseToken, cancellationToken);
        return true;
    }

    public async Task ProcessAsync(Guid requestId, Guid leaseToken, CancellationToken cancellationToken)
    {
        callLogs.Clear();
        diagnostics.Clear();
        var request = await ai.GetRequestAsync(requestId, cancellationToken);
        if (request is null || request.Status != AiRequestStatus.PROCESSING || request.LeaseToken != leaseToken)
            return; // claimed by someone else meanwhile; nothing to do

        var readsImages = false;
        try
        {
            var snapshot = JsonSerializer.Deserialize<AiInputSnapshot>(request.InputSnapshot, AiVersions.Json)
                ?? throw new InvalidOperationException("Input snapshot is empty.");
            readsImages = snapshot.Vision is not null;
            if (snapshot.SchemaVersion != AiVersions.Snapshot)
                throw new NonRetryableAiFailure(AiFailureCodes.SourceChanged, "This request was created by an earlier version of the app. Request it again.");

            string? warning = null;
            if (request.Kind == AiRequestKind.ImageGeneration)
            {
                await ImageAsync(request, snapshot, cancellationToken);
            }
            else
            {
                var connection = await ConnectionAsync(request, snapshot, cancellationToken);
                if (request.Kind == AiRequestKind.Generation)
                    warning = await GenerateAsync(request, snapshot, connection, cancellationToken);
                else
                    warning = await RewriteAsync(request, snapshot, connection, cancellationToken);
            }

            request.Complete(leaseToken, Now, warning);
            callLogs.ForEach(ai.Add);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            if (warning is not null)
                await systemLog.RecordAsync(SystemEventLevels.Warning, "AI Worker", "AI_REQUEST_PARTIAL",
                    $"{request.Kind} request {request.Id} completed with a warning: {warning}{(diagnostics.Count > 0 ? $" Details: {string.Join(" | ", diagnostics)}" : "")}",
                    request.CorrelationId, CancellationToken.None);
        }
        catch (LlmException ex)
        {
            var code = ex.Kind switch
            {
                LlmErrorKind.Authentication => AiFailureCodes.ProviderAuth,
                LlmErrorKind.Rejected => AiFailureCodes.ProviderRejected,
                _ => AiFailureCodes.ProviderUnavailable,
            };
            // A vision model that timed out will most likely time out again: fail at once (the user can pick another
            // model) instead of retrying for several minutes while the page shows "Analyzing images".
            var retryable = ex.Kind == LlmErrorKind.Transient && !readsImages;
            await FailAsync(requestId, leaseToken, code, ex.Message, retryable, cancellationToken);
        }
        catch (AiOutputInvalidException ex)
        {
            await FailAsync(requestId, leaseToken, AiFailureCodes.OutputInvalid, ex.Message, retryable: true, cancellationToken);
        }
        catch (NonRetryableAiFailure ex)
        {
            await FailAsync(requestId, leaseToken, ex.Code, ex.Message, retryable: false, cancellationToken);
        }
        catch (BusinessException ex) when (ex.Code == ErrorCodes.ValidationFailed)
        {
            // The generated text broke a deterministic rule enforced by the domain (e.g. length or hashtag limits).
            await FailAsync(requestId, leaseToken, AiFailureCodes.OutputInvalid, ex.Message, retryable: true, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Anything unexpected still ends the request at once (not retried: a bug would fail the same way again),
            // instead of leaving it PROCESSING until the lease expires and the page spins for minutes. The worker logs it.
            await FailAsync(requestId, leaseToken, AiFailureCodes.Unexpected, $"{ex.GetType().Name}: {ex.Message}", retryable: false, CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// Writes one draft per platform. With uploaded images a vision model writes them (all of them are attached for
    /// Text + Image); with an image plan the LLM-written image prompt is rendered (1-4 times) and attached. Returns a
    /// warning when the drafts were saved but an image could not be made or used.
    /// </summary>
    private async Task<string?> GenerateAsync(AiGenerationRequest request, AiInputSnapshot snapshot, LlmConnection connection, CancellationToken cancellationToken)
    {
        await ReportAsync(request, snapshot.Vision is null ? AiRequestStage.WRITING : AiRequestStage.ANALYZING_IMAGES, cancellationToken);
        var (imageUrls, uploaded) = await VisionImagesAsync(request, snapshot, cancellationToken);
        var raw = await CallAsync(request, AiCallPurpose.Generation, AiVersions.GenerationPrompt, connection,
            PromptBuilder.Generation(snapshot) with { ImageUrls = imageUrls }, cancellationToken);
        var variants = ParseLogged(request, () => OutputParser.ParseContent(raw, request.Platforms), raw);
        // "All platforms": the model is asked for identical variants; enforce it so every draft carries the same post.
        if (snapshot.Unified == true)
        {
            var shared = variants.Values.First();
            variants = variants.Keys.ToDictionary(p => p, _ => shared);
        }
        if (snapshot.Vision is not null)
            await ReportAsync(request, AiRequestStage.WRITING, cancellationToken);
        var evaluations = await EvaluateAsync(request, snapshot, variants, connection, cancellationToken, imageUrls);

        var imagePrompt = ImagePromptOf(raw);
        var (images, warning) = snapshot.ImagePlan is { } plan && (!plan.OnlyIfRequested || imagePrompt is not null)
            ? await PlannedImagesAsync(request, snapshot, plan, imagePrompt, ImageCountOf(raw), variants.Values.First(), cancellationToken)
            : (snapshot.Vision is { Attach: true } ? uploaded : [], null);
        // Every generation writes new drafts; nothing already under review or approved is touched.
        foreach (var (platform, variant) in variants)
        {
            // The same images go on every draft, each one only where its platform accepts it.
            var media = images.Where(image => Suits(image, platform)).ToList();
            if (media.Count < images.Count)
                warning ??= $"Some images were not attached to the {platform} draft because {platform} does not accept them.";
            // Version 1 already carries the destination (the first connected account, as Composer and AI Studio would pick),
            // so submitting the draft as it is does not first have to save a second version just to add the account.
            var destination = await DefaultDestinationAsync(request.OrganizationId, platform, cancellationToken);
            var (post, version) = Post.Create(Guid.NewGuid(), Guid.NewGuid(), request.OrganizationId, platform,
                Content(variant, null) with { Images = ImagesOf(media), SocialAccountId = destination?.Id }, media, destination,
                PostVersionSource.AiGeneration, request.RequestedByUserId, Now);
            posts.Add(post);
            posts.Add(version);
            activity.VersionCreated(post, version, request.RequestedByUserId, revokedApprovalId: null);
            Record(request, snapshot, platform, variant, version, evaluations);
        }
        return warning;
    }

    /// <summary>
    /// Text + Image: renders the LLM-written image prompt with the chosen image model, once per requested image (no
    /// transaction open). A failure does not lose the drafts: they keep the images that worked (possibly none) and the
    /// request completes with a warning the user sees.
    /// </summary>
    private async Task<(IReadOnlyList<MediaAsset> Images, string? Warning)> PlannedImagesAsync(
        AiGenerationRequest request, AiInputSnapshot snapshot, ImagePlanSnapshot plan, string? llmPrompt, int askedInPrompt, GeneratedVariant firstPost,
        CancellationToken cancellationToken)
    {
        var connection = await llmConnections.ResolveImageAsync(cancellationToken);
        if (connection is null || connection.Provider != plan.Provider)
        {
            diagnostics.Add($"Image generation was switched off or its provider changed (planned {plan.Provider}, now {connection?.Provider.ToString() ?? "none"}).");
            return ([], "No image was added this time.");
        }

        await ReportAsync(request, AiRequestStage.GENERATING_IMAGE, cancellationToken);
        // The picker and the user's own words ("make two images") can both ask for a number; the larger one wins.
        var count = Math.Max(plan.Count ?? 1, askedInPrompt);
        var prompt = PromptBuilder.GeneratedImage(snapshot, llmPrompt, firstPost);
        var stored = new List<MediaAsset>();
        var problem = false;
        // ponytail: sequential provider calls (at most 4); parallelize if image latency starts to threaten the 5-minute lease.
        for (var i = 0; i < count; i++)
        {
            try
            {
                var generated = await images.GenerateAsync(connection with { Model = plan.Model, Size = plan.Size }, Variation(prompt, i, count), cancellationToken);
                callLogs.Add(AiCallLog.Create(request, AiCallPurpose.ImageGeneration, AiVersions.GeneratedImagePrompt, true, null, generated.HttpStatus,
                    null, null, generated.LatencyMs, null, null, Now));
                stored.Add(await mediaService.StoreGeneratedAsync(request, null, generated.Bytes, cancellationToken, plan.Provider.ToString(), plan.Model));
            }
            catch (LlmException ex)
            {
                callLogs.Add(AiCallLog.Create(request, AiCallPurpose.ImageGeneration, AiVersions.GeneratedImagePrompt, false, ex.Kind.ToString(), ex.HttpStatus,
                    null, null, ex.LatencyMs, null, ex.Diagnostic ?? ex.Message, Now));
                // The provider's reason (model, HTTP status, billing text) is for system administrators only.
                diagnostics.Add($"Image {i + 1} of {count} ({plan.Provider} {plan.Model}): {ex.Kind} {ex.HttpStatus}: {ex.Diagnostic ?? ex.Message}");
                problem = true;
                break; // the provider is failing; more calls would most likely fail the same way
            }
            catch (BusinessException ex) when (ex.Code == ErrorCodes.ValidationFailed)
            {
                diagnostics.Add($"Image {i + 1} of {count} ({plan.Provider} {plan.Model}) was not usable: {ex.Message}");
                problem = true;
            }
        }
        if (!problem)
            return (stored, null);
        return (stored, stored.Count == 0
            ? "The image could not be created this time, so the posts were saved without one."
            : $"Only {stored.Count} of {count} images could be created this time.");
    }

    /// <summary>
    /// The same prompt for each requested image would return the same picture (providers cache identical requests and
    /// most run without a random seed), so every image after the first asks for a different composition.
    /// </summary>
    private static string Variation(string prompt, int index, int count) =>
        index == 0 ? prompt : $"{prompt}\n\nImage {index + 1} of {count}: show the same subject with a clearly different composition, angle and framing.";

    /// <summary>The workspace's first eligible account for the platform (same order the UI lists them), or null when none is connected.</summary>
    private async Task<SocialAccount?> DefaultDestinationAsync(Guid organizationId, SocialPlatform platform, CancellationToken cancellationToken)
    {
        var first = (await socialAccounts.ListAsync(organizationId, platform, cancellationToken)).FirstOrDefault(a => a.IsEligibleDestination);
        var account = first is null ? null : await socialAccounts.GetAsync(first.Id, cancellationToken);
        return account is { IsEligibleDestination: true } && account.OrganizationId == organizationId && account.Platform == platform ? account : null;
    }

    private static IReadOnlyList<PostImage> ImagesOf(IEnumerable<MediaAsset> media) => media.Select(m => new PostImage(m.Id)).ToList();

    private static bool Suits(MediaAsset image, SocialPlatform platform)
    {
        try
        {
            image.EnsureSuitableFor(platform);
            return true;
        }
        catch (BusinessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Saves the progress stage on its own so the page can show it. Called only before anything else is added to the unit
    /// of work, so it never commits partial results.
    /// </summary>
    private async Task ReportAsync(AiGenerationRequest request, AiRequestStage stage, CancellationToken cancellationToken)
    {
        request.ReportStage(request.LeaseToken!.Value, stage);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<string?> RewriteAsync(AiGenerationRequest request, AiInputSnapshot snapshot, LlmConnection connection, CancellationToken cancellationToken)
    {
        var post = await posts.GetAsync(request.PostId!.Value, cancellationToken);
        if (post is null || post.OrganizationId != request.OrganizationId)
            throw new NonRetryableAiFailure(AiFailureCodes.TargetNotEditable, "The post no longer exists.");
        // Never rewrite stale content: if someone saved a newer version meanwhile, the user must request again.
        if (post.CurrentVersionId != request.SourceVersionId)
            throw new NonRetryableAiFailure(AiFailureCodes.SourceChanged, "The post changed after the rewrite was requested. Request the rewrite again.");

        var raw = await CallAsync(request, AiCallPurpose.Rewrite, AiVersions.RewritePrompt, connection, PromptBuilder.Rewrite(snapshot), cancellationToken);
        var variants = ParseLogged(request, () => OutputParser.ParseContent(raw, request.Platforms), raw);
        var evaluations = await EvaluateAsync(request, snapshot, variants, connection, cancellationToken);

        var (platform, variant) = variants.Single();

        // The instruction asked for a new or changed image: render it and put it on the new version in place of the
        // current images (alt text starts empty).
        IReadOnlyList<MediaAsset>? newImages = null;
        string? warning = null;
        if (snapshot.ImagePlan is { } plan && ImagePromptOf(raw) is { } imagePrompt)
        {
            var (rendered, imageWarning) = await PlannedImagesAsync(request, snapshot, plan, imagePrompt, ImageCountOf(raw), variant, cancellationToken);
            newImages = rendered.Where(image => Suits(image, platform)).ToList();
            warning = imageWarning ?? (newImages.Count < rendered.Count ? $"A new image was not used because {platform} does not accept it." : null);
            if (newImages.Count == 0)
                newImages = null;
        }

        PostVersion version;
        try
        {
            version = await ReviseWithAsync(request, post,
                current => newImages is null ? Content(variant, current) : Content(variant, current) with { Images = ImagesOf(newImages) },
                newImages, PostVersionSource.AiRewrite, cancellationToken);
        }
        catch (AiOutputInvalidException) when (warning is not null)
        {
            // Only the image was asked for and it failed: report that instead of retrying an unchanged text.
            throw new NonRetryableAiFailure(AiFailureCodes.ProviderRejected, warning);
        }
        Record(request, snapshot, platform, variant, version, evaluations);
        return warning;
    }

    /// <summary>How many images the user's words asked for, per the model; 1 when the output does not say.</summary>
    private static int ImageCountOf(string raw)
    {
        try
        {
            return OutputParser.ParseImageCount(raw);
        }
        catch (AiOutputInvalidException)
        {
            return 1;
        }
    }

    /// <summary>The LLM-written image prompt, or null when it wrote none (or "" because the user did not ask for an image).</summary>
    private static string? ImagePromptOf(string raw)
    {
        try
        {
            return OutputParser.ParseImagePrompt(raw);
        }
        catch (AiOutputInvalidException)
        {
            return null;
        }
    }

    /// <summary>
    /// Creates the next version of the post. A rewrite or new image on approved content revokes the approval like any
    /// other edit.
    /// </summary>
    /// <param name="contentFor">The next version's content, built from the current version.</param>
    /// <param name="newImages">Replacement images (AI image generation); null keeps the current images.</param>
    private async Task<PostVersion> ReviseWithAsync(
        AiGenerationRequest request, Post post, Func<PostVersion, PostContent> contentFor, IReadOnlyList<MediaAsset>? newImages,
        PostVersionSource source, CancellationToken cancellationToken)
    {
        var current = await posts.GetVersionAsync(request.OrganizationId, post.Id, post.CurrentVersionId!.Value, cancellationToken)
            ?? throw new InvalidOperationException($"Post {post.Id} has no current version row.");
        var image = newImages ?? await media.GetImagesAsync(current, cancellationToken)
            ?? throw new NonRetryableAiFailure(AiFailureCodes.TargetNotEditable, "An image of the post no longer exists.");
        var destination = current.SocialAccountId is { } accountId ? await socialAccounts.GetAsync(accountId, cancellationToken) : null;
        var approvalBefore = post.ActiveApprovalId;
        try
        {
            var jobsToCancel = await publications.JobsToCancelForEditAsync(post, cancellationToken);
            var next = post.Revise(Guid.NewGuid(), current, contentFor(current), image, destination, source,
                    request.RequestedByUserId, Now)
                ?? throw new AiOutputInvalidException("The model returned content identical to the current version.", null);
            posts.Add(next);
            publications.CancelForEdit(jobsToCancel, next.Id, request.RequestedByUserId);
            activity.VersionCreated(post, next, request.RequestedByUserId, revokedApprovalId: approvalBefore);
            return next;
        }
        catch (BusinessException ex) when (ex.Code is ErrorCodes.InvalidStateTransition or ErrorCodes.PublicationTooLateToCancel)
        {
            throw new NonRetryableAiFailure(AiFailureCodes.TargetNotEditable, ex.Message);
        }
    }

    /// <summary>
    /// Image generation: the provider calls (one per requested image) happen with no transaction open; each image becomes
    /// an AI_GENERATED MediaAsset and together they replace the post's images in a new version with the text unchanged.
    /// Replaced images on approved content revoke the approval like any other edit, so they are always reviewed by a person.
    /// </summary>
    private async Task ImageAsync(AiGenerationRequest request, AiInputSnapshot snapshot, CancellationToken cancellationToken)
    {
        var post = await posts.GetAsync(request.PostId!.Value, cancellationToken);
        if (post is null || post.OrganizationId != request.OrganizationId)
            throw new NonRetryableAiFailure(AiFailureCodes.TargetNotEditable, "The post no longer exists.");
        if (post.CurrentVersionId != request.SourceVersionId)
            throw new NonRetryableAiFailure(AiFailureCodes.SourceChanged, "The post changed after the image was requested. Request it again.");
        var connection = await llmConnections.ResolveImageAsync(cancellationToken);
        if (connection is null || connection.Provider != request.Provider)
            throw new NonRetryableAiFailure(AiFailureCodes.NotConfigured, "Image generation was disabled or changed after this request. Request it again.");
        connection = connection with { Model = request.Model };

        var count = snapshot.Image?.Count ?? 1;
        var prompt = PromptBuilder.Image(snapshot);
        var assets = new List<MediaAsset>();
        // ponytail: sequential provider calls (at most 4); parallelize if image latency starts to threaten the 5-minute lease.
        for (var i = 0; i < count; i++)
        {
            GeneratedImage generated;
            try
            {
                generated = await images.GenerateAsync(connection, Variation(prompt, i, count), cancellationToken);
                callLogs.Add(AiCallLog.Create(request, AiCallPurpose.ImageGeneration, AiVersions.ImagePrompt, true, null, generated.HttpStatus, null, null,
                    generated.LatencyMs, null, null, Now));
            }
            catch (LlmException ex)
            {
                callLogs.Add(AiCallLog.Create(request, AiCallPurpose.ImageGeneration, AiVersions.ImagePrompt, false, ex.Kind.ToString(), ex.HttpStatus, null, null,
                    ex.LatencyMs, null, ex.Diagnostic ?? ex.Message, Now));
                throw;
            }

            try
            {
                assets.Add(await mediaService.StoreGeneratedAsync(request, post.Platform, generated.Bytes, cancellationToken));
            }
            catch (BusinessException ex) when (ex.Code == ErrorCodes.ValidationFailed)
            {
                // e.g. a PNG for Instagram (JPEG only) or an unsupported size: retrying the same model will not help.
                throw new NonRetryableAiFailure(AiFailureCodes.ImageNotSuitable, $"The generated image cannot be used on {post.Platform}: {ex.Message}");
            }
        }

        // Alt text describes a specific image, so new images start without the old descriptions; the reviewer sees that.
        var version = await ReviseWithAsync(request, post, current => new PostContent(current.Hook, current.Body, current.CallToAction,
            current.DestinationUrl, current.Hashtags, ImagesOf(assets), current.SocialAccountId), assets, PostVersionSource.AiImage, cancellationToken);
        ai.Add(AiRequestOutput.Create(request, version, post.Platform,
            JsonSerializer.Serialize(new { mediaAssetId = assets[0].Id, mediaAssetIds = assets.Select(a => a.Id) }, AiVersions.Json), Now));
    }

    /// <summary>One advisory evaluation call for all variants. Its failure never blocks the drafts; it becomes a visible warning.</summary>
    private async Task<IReadOnlyDictionary<SocialPlatform, VariantEvaluation>?> EvaluateAsync(
        AiGenerationRequest request, AiInputSnapshot snapshot, IReadOnlyDictionary<SocialPlatform, GeneratedVariant> variants,
        LlmConnection connection, CancellationToken cancellationToken, IReadOnlyList<string>? imageUrls = null)
    {
        try
        {
            var raw = await CallAsync(request, AiCallPurpose.Evaluation, AiVersions.EvaluationPrompt, connection,
                PromptBuilder.Evaluation(snapshot, variants) with { ImageUrls = imageUrls }, cancellationToken);
            return OutputParser.ParseEvaluation(raw);
        }
        catch (Exception ex) when (ex is LlmException or AiOutputInvalidException)
        {
            return null;
        }
    }

    private void Record(
        AiGenerationRequest request, AiInputSnapshot snapshot, SocialPlatform platform, GeneratedVariant variant, PostVersion version,
        IReadOnlyDictionary<SocialPlatform, VariantEvaluation>? evaluations)
    {
        var issues = DeterministicValidator.Validate(snapshot, platform, version, variant).ToList();
        VariantEvaluation? evaluation = null;
        if (evaluations is null)
            issues.Add(new EvaluationIssue(IssueSeverity.WARNING, "EVALUATION_UNAVAILABLE", "The advisory AI evaluation could not be completed for this version.", IssueSource.AI));
        else if (!evaluations.TryGetValue(platform, out evaluation))
            issues.Add(new EvaluationIssue(IssueSeverity.WARNING, "EVALUATION_UNAVAILABLE", $"The AI evaluation did not cover {platform}.", IssueSource.AI));
        if (evaluation is not null)
            issues.AddRange(AiIssues(evaluation));

        var scores = evaluation is null ? null
            : new EvaluationScores(evaluation.BrandAlignment, evaluation.AudienceRelevance, evaluation.Clarity, evaluation.PlatformFit, evaluation.CtaRelevance);
        ai.Add(ContentEvaluation.Create(version, request, scores, evaluation?.Summary, issues, request.Model, AiVersions.EvaluationPrompt, Now));
        ai.Add(AiRequestOutput.Create(request, version, platform, JsonSerializer.Serialize(variant, AiVersions.Json), Now));
    }

    /// <summary>
    /// Only factual-claim problems can be CRITICAL; style and fit findings are capped at WARNING so the model's opinion
    /// cannot block a human review on its own.
    /// </summary>
    private static IEnumerable<EvaluationIssue> AiIssues(VariantEvaluation evaluation)
    {
        foreach (var claim in evaluation.Claims ?? [])
        {
            if (claim.ContradictsFacts)
                yield return new(IssueSeverity.CRITICAL, "CONTRADICTORY_CLAIM", $"Contradicts the supplied facts: \"{claim.Text}\".", IssueSource.AI, claim.Text);
            else if (!claim.Supported)
                yield return new(IssueSeverity.CRITICAL, "UNSUPPORTED_CLAIM",
                    $"Unsupported {claim.Kind ?? "claim"}: \"{claim.Text}\". No supplied fact backs it; remove it or back it with evidence.", IssueSource.AI, claim.Text);
        }
        foreach (var finding in evaluation.Issues ?? [])
        {
            if (string.IsNullOrWhiteSpace(finding.Message))
                continue;
            var severity = string.Equals(finding.Severity, "INFO", StringComparison.OrdinalIgnoreCase) ? IssueSeverity.INFO : IssueSeverity.WARNING;
            var category = string.IsNullOrWhiteSpace(finding.Category) ? "STYLE" : finding.Category.Trim().ToUpperInvariant();
            yield return new(severity, category[..Math.Min(category.Length, 40)], finding.Message.Trim(), IssueSource.AI, finding.Excerpt);
        }
    }

    /// <summary>Model text becomes a version only through the domain; the link, images and destination are kept from the current version.</summary>
    private static PostContent Content(GeneratedVariant variant, PostVersion? current) => new(
        Hook: variant.Hook,
        Body: variant.Body!,
        CallToAction: variant.Cta,
        DestinationUrl: current?.DestinationUrl,
        Hashtags: variant.Hashtags,
        Images: current?.Images.OrderBy(i => i.Position).Select(i => new PostImage(i.MediaAssetId, i.AltText)).ToList(),
        SocialAccountId: current?.SocialAccountId);

    private async Task<string> CallAsync(
        AiGenerationRequest request, AiCallPurpose purpose, string promptVersion, LlmConnection connection, LlmChatRequest prompt, CancellationToken cancellationToken)
    {
        if (connection.MaxOutputTokens is { } maxTokens)
            prompt = prompt with { MaxOutputTokens = maxTokens };
        try
        {
            return await CallOnceAsync(request, purpose, promptVersion, connection, prompt, cancellationToken);
        }
        // A model the provider refuses (retired, misspelt) gets one try on the configured fallback model.
        catch (LlmException ex) when (ex.Kind == LlmErrorKind.Rejected && connection.FallbackModel is { Length: > 0 } fallback && fallback != connection.Model)
        {
            return await CallOnceAsync(request, purpose, promptVersion, connection with { Model = fallback }, prompt, cancellationToken);
        }
    }

    private async Task<string> CallOnceAsync(
        AiGenerationRequest request, AiCallPurpose purpose, string promptVersion, LlmConnection connection, LlmChatRequest prompt, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (connection.TimeoutSeconds is { } seconds)
            timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
        try
        {
            var response = await llm.CompleteJsonAsync(connection, prompt, timeout.Token);
            callLogs.Add(AiCallLog.Create(request, purpose, promptVersion, true, null, response.HttpStatus, response.InputTokens,
                response.OutputTokens, response.LatencyMs, estimatedCostUsd: null, diagnostic: null, Now));
            return response.Content;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // The configured request timeout elapsed: a transient failure, retried by the normal attempt policy.
            var timedOut = new LlmException(LlmErrorKind.Transient, $"The AI provider did not answer within {connection.TimeoutSeconds} seconds.", null,
                (connection.TimeoutSeconds ?? 0) * 1000, null);
            callLogs.Add(AiCallLog.Create(request, purpose, promptVersion, false, timedOut.Kind.ToString(), null, null, null, timedOut.LatencyMs, null, timedOut.Message, Now));
            throw timedOut;
        }
        catch (LlmException ex)
        {
            callLogs.Add(AiCallLog.Create(request, purpose, promptVersion, false, ex.Kind.ToString(), ex.HttpStatus, null, null,
                ex.LatencyMs, null, ex.Diagnostic ?? ex.Message, Now));
            throw;
        }
    }

    private T ParseLogged<T>(AiGenerationRequest request, Func<T> parse, string raw)
    {
        try
        {
            return parse();
        }
        catch (AiOutputInvalidException ex)
        {
            // Keep the rejected output only as truncated diagnostics on the call log, never as content.
            var log = callLogs[^1];
            callLogs[^1] = AiCallLog.Create(request, log.Purpose, log.PromptVersion, false, AiFailureCodes.OutputInvalid, log.HttpStatus,
                log.InputTokens, log.OutputTokens, log.LatencyMs, log.EstimatedCostUsd, $"{ex.Message}\n---\n{raw}", Now);
            throw;
        }
    }

    private async Task<LlmConnection> ConnectionAsync(AiGenerationRequest request, AiInputSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (snapshot.Vision is not null)
            return await llmConnections.ResolveHuggingFaceAsync(request.Model, cancellationToken)
                ?? throw new NonRetryableAiFailure(AiFailureCodes.NotConfigured, "The Hugging Face token was removed after this request. Request it again.");
        var connection = await llmConnections.ResolveAsync(request.OrganizationId, cancellationToken);
        if (connection is null || connection.Provider != request.Provider)
            throw new NonRetryableAiFailure(AiFailureCodes.NotConfigured, "The AI provider changed or was removed after this request. Request it again.");
        return connection with { Model = request.Model };
    }

    /// <summary>
    /// The uploaded images as data URIs for the vision model (read from storage on every attempt, never logged), and the
    /// assets themselves, which the new posts keep for Text + Image.
    /// </summary>
    private async Task<(IReadOnlyList<string>? Urls, IReadOnlyList<MediaAsset> Assets)> VisionImagesAsync(
        AiGenerationRequest request, AiInputSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (snapshot.Vision is not { } vision)
            return (null, []);
        var urls = new List<string>();
        var assets = new List<MediaAsset>();
        foreach (var id in vision.MediaAssetIds)
        {
            var asset = await media.GetAsync(id, cancellationToken);
            if (asset is null || asset.OrganizationId != request.OrganizationId)
                throw new NonRetryableAiFailure(AiFailureCodes.SourceChanged, "An uploaded image is no longer available. Upload it again.");
            await using var stream = await mediaStorage.OpenReadAsync(asset.ObjectKey, cancellationToken);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            urls.Add($"data:{asset.ContentType};base64,{Convert.ToBase64String(buffer.GetBuffer(), 0, (int)buffer.Length)}");
            assets.Add(asset);
        }
        return (urls, assets);
    }

    /// <summary>Drops any partial work, then records the failure (and the call logs) under the lease.</summary>
    private async Task FailAsync(Guid requestId, Guid leaseToken, string code, string message, bool retryable, CancellationToken cancellationToken)
    {
        var logs = callLogs.ToList();
        unitOfWork.DiscardChanges();
        var request = await ai.GetRequestAsync(requestId, cancellationToken);
        if (request is null || request.Status != AiRequestStatus.PROCESSING || request.LeaseToken != leaseToken)
            return;
        request.Fail(leaseToken, code, message, retryable, Now);
        logs.ForEach(ai.Add);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        // The customer sees a plain sentence (AiGenerationRequestDto); the admin sees exactly what happened.
        await systemLog.RecordAsync(retryable ? SystemEventLevels.Warning : SystemEventLevels.Error, "AI Worker",
            retryable ? "AI_REQUEST_RETRYING" : "AI_REQUEST_FAILED",
            $"{request.Kind} request {request.Id} (attempt {request.AttemptCount}/{request.MaxAttempts}) {code}: {message}"
            + (diagnostics.Count > 0 ? $" Details: {string.Join(" | ", diagnostics)}" : ""),
            request.CorrelationId, CancellationToken.None);
    }

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    private sealed class NonRetryableAiFailure(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }
}
