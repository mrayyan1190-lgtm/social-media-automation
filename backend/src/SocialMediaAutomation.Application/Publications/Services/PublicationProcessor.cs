using System.Text.Json;
using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.Audit.Services;
using SocialMediaAutomation.Domain.Common;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Application.Publications.Services;

/// <summary>
/// The publishing worker's pipeline (ARCHITECTURE.md §29-34). Jobs are claimed by a short SQL statement, then each job is
/// revalidated, prepared and dispatched with only auto-committed saves in between: no transaction or lock is held while
/// a provider is called. Every save is fenced by the lease token and the row version, so a worker that lost its lease
/// can never overwrite newer state.
/// </summary>
public sealed class PublicationProcessor(
    IPublicationWorkQueue queue,
    IPublicationRepository publications,
    IOrganizationRepository organizations,
    IPostRepository posts,
    IReviewRepository reviews,
    ISocialAccountRepository socialAccounts,
    IMediaRepository media,
    IMediaStorage storage,
    IOAuthRepository oauth,
    ISecretProtector secrets,
    IEnumerable<ISocialPublisher> publishers,
    ActivityRecorder activity,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MediaPollInterval = TimeSpan.FromSeconds(30);
    public const int BatchSize = 5;

    private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Settles PROCESSING jobs whose worker vanished: after a real dispatch the outcome is unknown (reconcile), on the
    /// final attempt before dispatch the job fails. Everything else with an expired lease is reclaimed by the claim query.
    /// </summary>
    public async Task<int> SweepAbandonedAsync(CancellationToken cancellationToken)
    {
        var settled = 0;
        foreach (var job in await publications.ListExpiredLeasesAsync(Now, 20, cancellationToken))
        {
            if (!job.ReconcileAbandoned(Now) && !job.FailAbandoned(Now))
                continue;
            if (await publications.FindOpenAttemptAsync(job.Id, cancellationToken) is { } open)
                open.Finish(job.DeliveryState, Now, Category(job.DeliveryState), job.LastErrorCode, job.LastErrorMessage);
            Announce(job, receipt: null);
            settled++;
        }
        if (settled == 0)
            return 0;
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (BusinessException ex) when (ex.Code == ErrorCodes.ConcurrencyConflict)
        {
            // The original worker finished at the same moment; its result wins. The next sweep re-checks.
            unitOfWork.DiscardChanges();
            return 0;
        }
        return settled;
    }

    public Task<IReadOnlyList<(Guid JobId, Guid LeaseToken)>> ClaimAsync(string workerId, CancellationToken cancellationToken) =>
        queue.ClaimDueAsync(workerId, BatchSize, LeaseDuration, cancellationToken);

    /// <summary>Processes one claimed job. Call it in a fresh scope per job.</summary>
    public async Task ProcessAsync(Guid jobId, Guid leaseToken, string workerId, CancellationToken cancellationToken)
    {
        var job = await publications.GetAsync(jobId, cancellationToken);
        if (job is null || job.DeliveryState != DeliveryState.PROCESSING || job.LeaseToken != leaseToken)
            return; // another worker owns it now

        var attempt = PublicationAttempt.Start(job, job.ProviderContainerId is null ? PublicationAttemptStep.DISPATCH : PublicationAttemptStep.MEDIA_STATUS,
            workerId, Now);
        publications.Add(attempt);
        var sent = false;
        PublishReceipt receipt;
        try
        {
            var context = await RevalidateAsync(job, cancellationToken);
            var publisher = Select(job);
            await publisher.ValidateCapabilityAsync(context, cancellationToken);
            if (job.IsLate(Now))
                throw new PublishFailure(PublishErrorCategory.ACTION_REQUIRED, PublicationErrorCodes.TooLate,
                    $"The publication was due at {job.PublishAtUtc:yyyy-MM-dd HH:mm} UTC and is more than {PublicationJob.MaxLateness.TotalHours:0} hours late; it was not sent automatically.");

            if (publisher.UsesMediaContainer)
            {
                if (job.ProviderContainerId is null)
                {
                    var containerId = await publisher.PrepareAsync(context, cancellationToken);
                    job.RecordMediaContainer(leaseToken, containerId, Now);
                    await unitOfWork.SaveChangesAsync(cancellationToken); // checkpoint survives a restart
                    context = context with { ContainerId = containerId };
                }
                switch (await publisher.GetStatusAsync(context, cancellationToken))
                {
                    case MediaContainerStatus.Failed:
                        throw new PublishFailure(PublishErrorCategory.ACTION_REQUIRED, PublicationErrorCodes.MediaProcessingFailed,
                            "The provider could not process the image. Replace the image (new version) or check the destination.");
                    case MediaContainerStatus.InProgress when job.MediaWaitExpired(Now):
                        throw new PublishFailure(PublishErrorCategory.ACTION_REQUIRED, PublicationErrorCodes.MediaProcessingTimeout,
                            $"The provider did not finish processing the image within {PublicationJob.MaxMediaWait.TotalMinutes:0} minutes.");
                    case MediaContainerStatus.InProgress:
                        // Never sleep here: release the job and let a later claim check again.
                        job.WaitForMedia(leaseToken, Now.Add(MediaPollInterval), Now);
                        attempt.Finish(DeliveryState.WAITING_MEDIA, Now, detail: $"Media container {job.ProviderContainerId} is still processing.");
                        await unitOfWork.SaveChangesAsync(cancellationToken);
                        return;
                }
            }

            job.MarkDispatchStarted(leaseToken, Now);
            await unitOfWork.SaveChangesAsync(cancellationToken); // fenced checkpoint: fails here if the lease was lost, before sending
            sent = true;
            receipt = await publisher.PublishAsync(context, cancellationToken);
        }
        catch (BusinessException ex) when (ex.Code == ErrorCodes.ConcurrencyConflict && !sent)
        {
            unitOfWork.DiscardChanges(); // lost the lease before sending; the new owner continues
            return;
        }
        catch (PublishFailure failure)
        {
            await RecordFailureAsync(job, attempt, leaseToken, failure, sent, cancellationToken);
            return;
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            // Not a normalized provider error (a bug or infrastructure failure). Before sending, retrying is safe; after,
            // the post may exist.
            var failure = sent
                ? new PublishFailure(PublishErrorCategory.UNCERTAIN, PublicationErrorCodes.UnexpectedAfterSend,
                    $"Unexpected error after the publishing call started ({ex.GetType().Name}); the post may exist.")
                : new PublishFailure(PublishErrorCategory.RETRYABLE, PublicationErrorCodes.UnexpectedBeforeSend, $"Unexpected error before sending ({ex.GetType().Name}).");
            await RecordFailureAsync(job, attempt, leaseToken, failure, sent, cancellationToken);
            return;
        }

        await RecordSuccessAsync(job, attempt, leaseToken, receipt, cancellationToken);
    }

    // ---- Revalidation ---------------------------------------------------------------------------------------

    /// <summary>Immediately before dispatch: everything that was true at scheduling time is checked again (ARCHITECTURE.md §30).</summary>
    private async Task<PublishContext> RevalidateAsync(PublicationJob job, CancellationToken cancellationToken)
    {
        var organization = await organizations.GetAsync(job.OrganizationId, cancellationToken);
        if (organization is null || organization.Status != OrganizationStatus.Active)
            throw Blocked(PublicationErrorCodes.WorkspaceInactive, "The workspace is not active.");

        var post = await posts.GetAsync(job.PostId, cancellationToken);
        if (post is null || post.OrganizationId != job.OrganizationId || post.EditorialState != EditorialState.APPROVED
            || post.CurrentVersionId != job.PostVersionId || post.ActiveApprovalId != job.ReviewRequestId)
            throw Blocked(ErrorCodes.ApprovalStale, "The approval no longer covers this version of the post.");
        var approval = await reviews.GetAsync(job.ReviewRequestId, cancellationToken);
        if (approval is null || approval.Status != ReviewStatus.APPROVED || approval.ApprovedPayloadHash != job.PayloadHash)
            throw Blocked(ErrorCodes.ApprovalStale, "The approval behind this publication is no longer valid.");
        var version = await posts.GetVersionAsync(job.OrganizationId, job.PostId, job.PostVersionId, cancellationToken)
            ?? throw Blocked(ErrorCodes.ApprovalStale, "The approved version no longer exists.");

        var account = await socialAccounts.GetAsync(job.SocialAccountId, cancellationToken);
        if (account is null || account.OrganizationId != job.OrganizationId || account.Id != version.SocialAccountId)
            throw Blocked(ErrorCodes.SocialAccountNotReady, "The approved destination no longer exists.");
        if (account.ExecutionMode != job.ExecutionMode)
            throw Blocked(ErrorCodes.SocialAccountNotReady, $"The destination is now {account.ExecutionMode}, not {job.ExecutionMode} as when this was scheduled.");
        if (!account.IsEligibleDestination)
            throw Blocked(ErrorCodes.SocialAccountNotReady, $"The destination \"{account.DisplayName}\" is {account.ConnectionStatus}; reconnect it.");

        MediaAsset? image = null;
        if (version.MediaAssetId is { } mediaId)
        {
            image = await media.GetAsync(mediaId, cancellationToken);
            if (image is null || image.OrganizationId != job.OrganizationId)
                throw Blocked(PublicationErrorCodes.MediaMissing, "The approved image no longer exists.");
            try
            {
                image.EnsureSuitableFor(job.Platform);
                await using var _ = await storage.OpenReadAsync(image.ObjectKey, cancellationToken);
            }
            catch (BusinessException ex)
            {
                throw Blocked(PublicationErrorCodes.MediaInvalid, ex.Message);
            }
            catch (IOException)
            {
                throw Blocked(PublicationErrorCodes.MediaMissing, "The approved image file is missing from storage.");
            }
        }
        else if (PlatformRules.For(job.Platform).RequiresImage)
        {
            throw Blocked(PublicationErrorCodes.MediaMissing, $"{job.Platform} posts require one image.");
        }

        // The stored data must still hash to the approval, and so must the frozen snapshot the publisher will send.
        var frozen = JsonSerializer.Deserialize<ApprovalPayload>(job.PayloadJson, PayloadJson);
        if (!ApprovalPayload.From(post, version, account, image).Matches(job.PayloadHash) || frozen is null || !frozen.Matches(job.PayloadHash))
            throw Blocked(ErrorCodes.ApprovalStale, "The content no longer matches the approved payload.");

        return new PublishContext(job.Id, job.Platform, job.ExecutionMode, account.ExternalAccountId, account.Capabilities, frozen,
            image is null ? null : new PublishMedia(image.Id, image.ContentType, image.Sha256, version.AltText, ct => storage.OpenReadAsync(image.ObjectKey, ct), null),
            await AccessTokenAsync(job, account, cancellationToken), job.ProviderContainerId);
    }

    private async Task<string?> AccessTokenAsync(PublicationJob job, SocialAccount account, CancellationToken cancellationToken)
    {
        if (job.ExecutionMode is not (ExecutionMode.LIVE or ExecutionMode.TEST_ACCOUNT))
            return null;
        var connection = account.OAuthConnectionId is { } connectionId ? await oauth.GetConnectionAsync(connectionId, cancellationToken) : null;
        if (connection is null || connection.OrganizationId != job.OrganizationId || connection.Status != OAuthConnectionStatus.ACTIVE
            || connection.ProtectedAccessToken.Length == 0)
            throw Blocked(PublicationErrorCodes.AuthRevoked, $"The {job.Platform} connection was revoked; reconnect the destination.");
        if (connection.AccessTokenExpiresAtUtc is { } expires && expires <= Now)
            throw Blocked(PublicationErrorCodes.AuthExpired, $"The {job.Platform} authorization expired on {expires:yyyy-MM-dd HH:mm} UTC; reconnect the destination.");
        return secrets.Unprotect(connection.ProtectedAccessToken);
    }

    /// <summary>Exactly one publisher serves a platform and mode; a LIVE job never falls back to the simulation.</summary>
    private ISocialPublisher Select(PublicationJob job)
    {
        var candidates = publishers.Where(p => p.CanPublish(job.Platform, job.ExecutionMode)).ToList();
        return candidates.Count == 1
            ? candidates[0]
            : throw Blocked(PublicationErrorCodes.NoPublisher, $"No publisher is available for {job.Platform} in {job.ExecutionMode} mode.");
    }

    // ---- Outcomes -------------------------------------------------------------------------------------------

    private async Task RecordSuccessAsync(PublicationJob job, PublicationAttempt attempt, Guid leaseToken, PublishReceipt receipt, CancellationToken cancellationToken)
    {
        if (job.ExecutionMode == ExecutionMode.SIMULATED)
            job.CompleteSimulated(leaseToken, Now);
        else if (receipt.ExternalId is null)
            job.RequireReconciliation(leaseToken, PublicationErrorCodes.NoExternalId,
                "The provider reported success but returned no post id; confirm on the destination.", null, Now);
        else
            job.CompletePublished(leaseToken, receipt.ExternalId, receipt.ExternalUrl, Now);
        attempt.Finish(job.DeliveryState, Now, providerRequestId: receipt.ProviderRequestId, externalId: receipt.ExternalId, detail: receipt.Detail);
        Announce(job, receipt);
        await SaveOutcomeAsync(job, attempt, leaseToken, sent: true, receipt.ExternalId, cancellationToken);
    }

    private async Task RecordFailureAsync(
        PublicationJob job, PublicationAttempt attempt, Guid leaseToken, PublishFailure failure, bool sent, CancellationToken cancellationToken)
    {
        // Nothing can have been published before the publishing call, so "uncertain" there just means "try again".
        var category = !sent && failure.Category == PublishErrorCategory.UNCERTAIN ? PublishErrorCategory.RETRYABLE : failure.Category;
        if (job.ExecutionMode == ExecutionMode.SIMULATED && category == PublishErrorCategory.UNCERTAIN)
            category = PublishErrorCategory.RETRYABLE; // a simulation has no external side effect to reconcile
        switch (category)
        {
            case PublishErrorCategory.RETRYABLE:
                job.ScheduleRetry(leaseToken, failure.Code, failure.Message, Now);
                break;
            case PublishErrorCategory.ACTION_REQUIRED:
                job.RequireAction(leaseToken, failure.Code, failure.Message, Now);
                break;
            case PublishErrorCategory.UNCERTAIN:
                job.RequireReconciliation(leaseToken, failure.Code, failure.Message, null, Now);
                break;
            default:
                job.Fail(leaseToken, failure.Code, failure.Message, Now);
                break;
        }
        attempt.Finish(job.DeliveryState, Now, category, failure.Code, failure.Message, failure.ProviderRequestId);
        Announce(job, receipt: null);
        await SaveOutcomeAsync(job, attempt, leaseToken, sent, null, cancellationToken);
    }

    /// <summary>
    /// Saves the outcome. If that fails after a real send (the provider may have created the post), the job is moved to
    /// RECONCILIATION_REQUIRED with whatever evidence we have, instead of ever being sent again.
    /// </summary>
    private async Task SaveOutcomeAsync(
        PublicationJob job, PublicationAttempt attempt, Guid leaseToken, bool sent, string? evidenceExternalId, CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return;
        }
        catch (Exception) when (!sent || job.ExecutionMode == ExecutionMode.SIMULATED)
        {
            // Nothing external happened: drop our view; the lease owner (or a reclaim after expiry) continues.
            unitOfWork.DiscardChanges();
            return;
        }
        catch (Exception ex)
        {
            unitOfWork.DiscardChanges();
            var message = $"The provider call completed{(evidenceExternalId is null ? "" : $" (provider id {evidenceExternalId})")} but the result could not be saved ({ex.GetType().Name}). Confirm on the destination.";
            var fresh = await publications.GetAsync(job.Id, cancellationToken);
            if (fresh is { DeliveryState: DeliveryState.PROCESSING } && fresh.LeaseToken == leaseToken)
            {
                fresh.RequireReconciliation(leaseToken, PublicationErrorCodes.PersistenceFailedAfterSend, message, evidenceExternalId, Now);
                Announce(fresh, receipt: null);
            }
            // Keep the evidence even if a sweep already settled the job and closed our attempt.
            var evidence = await publications.FindOpenAttemptAsync(job.Id, cancellationToken);
            if (evidence is null && fresh is not null)
            {
                evidence = PublicationAttempt.Start(fresh, attempt.Step, attempt.WorkerId, Now);
                publications.Add(evidence);
            }
            evidence?.Finish(fresh?.DeliveryState ?? DeliveryState.RECONCILIATION_REQUIRED, Now, PublishErrorCategory.UNCERTAIN,
                PublicationErrorCodes.PersistenceFailedAfterSend, message, attempt.ProviderRequestId, evidenceExternalId);
            // If this save fails too, the dispatch checkpoint is already stored: the abandoned-lease sweep reconciles it.
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>Notification and audit for outcomes a person should know about. Retries stay in the attempt history only.</summary>
    private void Announce(PublicationJob job, PublishReceipt? receipt)
    {
        if (Headline(job) is not var (type, action, title))
            return;
        var message = job.DeliveryState switch
        {
            DeliveryState.PUBLISHED => receipt?.ExternalUrl is { } url ? $"The approved version is live: {url}" : "The approved version was published.",
            DeliveryState.SIMULATED => receipt?.Detail ?? "The simulated destination completed; nothing was posted externally.",
            _ => job.LastErrorMessage ?? job.DeliveryState.ToString(),
        };
        activity.Notify(job.OrganizationId, [job.CreatedByUserId], type, title, message, PublicationService.JobResource, job.Id);
        activity.Audit(job.OrganizationId, null, action, PublicationService.JobResource, job.Id, new Dictionary<string, string?>
        {
            ["postId"] = job.PostId.ToString(), ["postVersionId"] = job.PostVersionId.ToString(), ["executionMode"] = job.ExecutionMode.ToString(),
            ["attempt"] = job.AttemptCount.ToString(), ["externalPostId"] = job.ExternalPostId, ["errorCode"] = job.LastErrorCode,
        });
    }

    private static (NotificationType Type, string Action, string Title)? Headline(PublicationJob job) => job.DeliveryState switch
    {
        DeliveryState.PUBLISHED => (NotificationType.PUBLISHED, AuditActions.PublicationPublished, $"Published to {job.Platform}"),
        DeliveryState.SIMULATED => (NotificationType.SIMULATED, AuditActions.PublicationSimulated, $"Simulated {job.Platform} publication (not posted)"),
        DeliveryState.FAILED => (NotificationType.FAILED, AuditActions.PublicationFailed, $"{job.Platform} publication failed"),
        DeliveryState.ACTION_REQUIRED => (NotificationType.ACTION_REQUIRED, AuditActions.PublicationActionRequired, $"{job.Platform} publication needs attention"),
        DeliveryState.RECONCILIATION_REQUIRED => (NotificationType.RECONCILIATION_REQUIRED, AuditActions.PublicationReconciliationRequired,
            $"Check {job.Platform}: publication outcome unknown"),
        _ => null,
    };

    private static PublishErrorCategory? Category(DeliveryState state) => state switch
    {
        DeliveryState.RECONCILIATION_REQUIRED => PublishErrorCategory.UNCERTAIN,
        DeliveryState.FAILED => PublishErrorCategory.PERMANENT,
        _ => null,
    };

    private static PublishFailure Blocked(string code, string message) => new(PublishErrorCategory.ACTION_REQUIRED, code, message);

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;
}
