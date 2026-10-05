using System.Text.Json;
using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.AiGeneration.Contracts;
using SocialMediaAutomation.Application.Audit.Services;
using SocialMediaAutomation.Application.Media.Services;
using SocialMediaAutomation.Application.AiGeneration.Prompts;
using SocialMediaAutomation.Application.AiGeneration.Validation;
using SocialMediaAutomation.Application.Publications.Services;
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
    ICampaignRepository campaigns,
    IMediaRepository media,
    ISocialAccountRepository socialAccounts,
    ActivityRecorder activity,
    PublicationService publications,
    AiConnectionResolver llmConnections,
    ILlmClient llm,
    IImageGenerationProvider images,
    MediaService mediaService,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);

    private readonly List<AiCallLog> callLogs = [];

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
        var request = await ai.GetRequestAsync(requestId, cancellationToken);
        if (request is null || request.Status != AiRequestStatus.PROCESSING || request.LeaseToken != leaseToken)
            return; // claimed by someone else meanwhile; nothing to do

        try
        {
            var snapshot = JsonSerializer.Deserialize<AiInputSnapshot>(request.InputSnapshot, AiVersions.Json)
                ?? throw new InvalidOperationException("Input snapshot is empty.");

            if (request.Kind == AiRequestKind.ImageGeneration)
            {
                await ImageAsync(request, snapshot, cancellationToken);
            }
            else
            {
                var connection = await ConnectionAsync(request, cancellationToken);
                if (request.Kind == AiRequestKind.Generation)
                    await GenerateAsync(request, snapshot, connection, cancellationToken);
                else
                    await RewriteAsync(request, snapshot, connection, cancellationToken);
            }

            request.Complete(leaseToken, Now);
            callLogs.ForEach(ai.Add);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (LlmException ex)
        {
            var code = ex.Kind switch
            {
                LlmErrorKind.Authentication => AiFailureCodes.ProviderAuth,
                LlmErrorKind.Rejected => AiFailureCodes.ProviderRejected,
                _ => AiFailureCodes.ProviderUnavailable,
            };
            await FailAsync(requestId, leaseToken, code, ex.Message, retryable: ex.Kind == LlmErrorKind.Transient, cancellationToken);
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
    }

    private async Task GenerateAsync(AiGenerationRequest request, AiInputSnapshot snapshot, LlmConnection connection, CancellationToken cancellationToken)
    {
        var raw = await CallAsync(request, AiCallPurpose.Generation, AiVersions.GenerationPrompt, connection, PromptBuilder.Generation(snapshot), cancellationToken);
        var variants = ParseLogged(request, () => OutputParser.ParseContent(raw, request.Platforms), raw);
        var evaluations = await EvaluateAsync(request, snapshot, variants, connection, cancellationToken);
        var campaign = await campaigns.GetAsync(request.CampaignId, cancellationToken)
            ?? throw new NonRetryableAiFailure(AiFailureCodes.TargetNotEditable, "The campaign no longer exists.");

        foreach (var (platform, variant) in variants)
        {
            var existing = await posts.FindActivePostAsync(request.OrganizationId, request.CampaignId, platform, cancellationToken);
            PostVersion version;
            if (existing is null)
            {
                var (post, first) = Post.Create(Guid.NewGuid(), Guid.NewGuid(), campaign, platform, Content(snapshot, variant, null),
                    null, null, PostVersionSource.AiGeneration, request.RequestedByUserId, Now);
                posts.Add(post);
                posts.Add(first);
                activity.VersionCreated(post, first, request.RequestedByUserId, revokedApprovalId: null);
                version = first;
            }
            else
            {
                if (existing.EditorialState is not (EditorialState.DRAFT or EditorialState.CHANGES_REQUESTED))
                    throw new NonRetryableAiFailure(AiFailureCodes.TargetNotEditable, $"The {platform} post moved to {existing.EditorialState} while generating.");
                version = await ReviseAsync(request, existing, campaign, snapshot, variant, PostVersionSource.AiGeneration, cancellationToken);
            }
            Record(request, snapshot, platform, variant, version, evaluations);
        }
    }

    private async Task RewriteAsync(AiGenerationRequest request, AiInputSnapshot snapshot, LlmConnection connection, CancellationToken cancellationToken)
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
        var campaign = await campaigns.GetAsync(request.CampaignId, cancellationToken)
            ?? throw new NonRetryableAiFailure(AiFailureCodes.TargetNotEditable, "The campaign no longer exists.");

        var (platform, variant) = variants.Single();
        var version = await ReviseAsync(request, post, campaign, snapshot, variant, PostVersionSource.AiRewrite, cancellationToken);
        Record(request, snapshot, platform, variant, version, evaluations);
    }

    /// <summary>
    /// Creates the next version from model text, keeping the current image, alt text and destination. A rewrite of
    /// approved content revokes the approval like any other edit.
    /// </summary>
    private Task<PostVersion> ReviseAsync(
        AiGenerationRequest request, Post post, Campaign campaign, AiInputSnapshot snapshot, GeneratedVariant variant,
        PostVersionSource source, CancellationToken cancellationToken) =>
        ReviseWithAsync(request, post, campaign, current => Content(snapshot, variant, current), newImage: null, source, cancellationToken);

    /// <param name="contentFor">The next version's content, built from the current version.</param>
    /// <param name="newImage">A replacement image (AI image generation); null keeps the current image.</param>
    private async Task<PostVersion> ReviseWithAsync(
        AiGenerationRequest request, Post post, Campaign campaign, Func<PostVersion, PostContent> contentFor, MediaAsset? newImage,
        PostVersionSource source, CancellationToken cancellationToken)
    {
        var current = await posts.GetVersionAsync(request.OrganizationId, post.Id, post.CurrentVersionId!.Value, cancellationToken)
            ?? throw new InvalidOperationException($"Post {post.Id} has no current version row.");
        var image = newImage ?? (current.MediaAssetId is { } mediaId ? await media.GetAsync(mediaId, cancellationToken) : null);
        var destination = current.SocialAccountId is { } accountId ? await socialAccounts.GetAsync(accountId, cancellationToken) : null;
        var approvalBefore = post.ActiveApprovalId;
        try
        {
            var jobsToCancel = await publications.JobsToCancelForEditAsync(post, cancellationToken);
            var next = post.Revise(Guid.NewGuid(), current, campaign, contentFor(current), image, destination, source,
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
    /// Image generation: the provider call happens with no transaction open; the image becomes an AI_GENERATED MediaAsset
    /// and a new version of the post with the text unchanged. A replaced image on approved content revokes the approval
    /// like any other edit, so the image is always reviewed by a person.
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

        GeneratedImage generated;
        try
        {
            generated = await images.GenerateAsync(connection, PromptBuilder.Image(snapshot), cancellationToken);
            callLogs.Add(AiCallLog.Create(request, AiCallPurpose.ImageGeneration, AiVersions.ImagePrompt, true, null, generated.HttpStatus, null, null,
                generated.LatencyMs, null, null, Now));
        }
        catch (LlmException ex)
        {
            callLogs.Add(AiCallLog.Create(request, AiCallPurpose.ImageGeneration, AiVersions.ImagePrompt, false, ex.Kind.ToString(), ex.HttpStatus, null, null,
                ex.LatencyMs, null, ex.Diagnostic ?? ex.Message, Now));
            throw;
        }

        var campaign = await campaigns.GetAsync(request.CampaignId, cancellationToken)
            ?? throw new NonRetryableAiFailure(AiFailureCodes.TargetNotEditable, "The campaign no longer exists.");
        MediaAsset asset;
        try
        {
            asset = await mediaService.StoreGeneratedAsync(request, post.Platform, generated.Bytes, cancellationToken);
        }
        catch (BusinessException ex) when (ex.Code == ErrorCodes.ValidationFailed)
        {
            // e.g. a PNG for Instagram (JPEG only) or an unsupported size: retrying the same model will not help.
            throw new NonRetryableAiFailure(AiFailureCodes.ImageNotSuitable, $"The generated image cannot be used on {post.Platform}: {ex.Message}");
        }

        // Alt text describes a specific image, so the new image starts without the old description; the reviewer sees that.
        var version = await ReviseWithAsync(request, post, campaign, current => new PostContent(current.Hook, current.Body, current.CallToAction,
            current.DestinationUrl, current.Hashtags, asset.Id, null, current.SocialAccountId), asset, PostVersionSource.AiImage, cancellationToken);
        ai.Add(AiRequestOutput.Create(request, version, post.Platform, JsonSerializer.Serialize(new { mediaAssetId = asset.Id }, AiVersions.Json), Now));
    }

    /// <summary>One advisory evaluation call for all variants. Its failure never blocks the drafts; it becomes a visible warning.</summary>
    private async Task<IReadOnlyDictionary<SocialPlatform, VariantEvaluation>?> EvaluateAsync(
        AiGenerationRequest request, AiInputSnapshot snapshot, IReadOnlyDictionary<SocialPlatform, GeneratedVariant> variants,
        LlmConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            var raw = await CallAsync(request, AiCallPurpose.Evaluation, AiVersions.EvaluationPrompt, connection,
                PromptBuilder.Evaluation(snapshot, variants), cancellationToken);
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
                    $"Unsupported {claim.Kind ?? "claim"}: \"{claim.Text}\". No supplied fact backs it; remove it or add evidence as a campaign fact.", IssueSource.AI, claim.Text);
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

    /// <summary>Model text becomes a version only through the domain; the destination URL always comes from the campaign brief.</summary>
    private static PostContent Content(AiInputSnapshot snapshot, GeneratedVariant variant, PostVersion? current) => new(
        Hook: variant.Hook,
        Body: variant.Body!,
        CallToAction: variant.Cta ?? snapshot.Campaign.CallToActionText,
        DestinationUrl: snapshot.Campaign.CallToActionUrl,
        Hashtags: variant.Hashtags,
        MediaAssetId: current?.MediaAssetId,
        AltText: current?.AltText,
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

    private async Task<LlmConnection> ConnectionAsync(AiGenerationRequest request, CancellationToken cancellationToken)
    {
        var connection = await llmConnections.ResolveAsync(request.OrganizationId, cancellationToken);
        if (connection is null || connection.Provider != request.Provider)
            throw new NonRetryableAiFailure(AiFailureCodes.NotConfigured, "The AI provider changed or was removed after this request. Request it again.");
        return connection with { Model = request.Model };
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
    }

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    private sealed class NonRetryableAiFailure(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }
}
