using SocialMediaAutomation.Domain.Common;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Domain.Entities;

/// <summary>Delivery state of one publication intent (PRD §28). Editorial state never lives here.</summary>
public enum DeliveryState
{
    SCHEDULED,
    PROCESSING,
    WAITING_MEDIA,
    RETRY_PENDING,
    ACTION_REQUIRED,
    RECONCILIATION_REQUIRED,
    PUBLISHED,
    SIMULATED,
    FAILED,
    CANCELED,
}

/// <summary>
/// When a job is due: the canonical UTC instant plus the user's original wall-clock input. All three local fields are
/// null for Publish Now. The offset is the one in force at that local time; for an ambiguous DST time it is the one the
/// user explicitly chose.
/// </summary>
public sealed record PublishTime(DateTime Utc, DateTime? Local, string? TimeZoneId, int? UtcOffsetMinutes)
{
    public static readonly TimeSpan MinimumLead = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaximumHorizon = TimeSpan.FromDays(366);

    public static PublishTime Now(DateTime utcNow) => new(utcNow, null, null, null);

    /// <summary>
    /// Resolves a local date/time in an IANA zone to UTC. A time skipped by a DST jump is rejected; a time that occurs
    /// twice is rejected unless the caller picked one of its two offsets.
    /// </summary>
    public static PublishTime Resolve(DateOnly date, TimeOnly time, string? timeZoneId, int? chosenOffsetMinutes, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId) || !TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId.Trim(), out var zone) || !zone.HasIanaId)
            throw Guard.Invalid("Time zone", "must be an IANA time zone such as 'Europe/London' or 'America/New_York'");

        var local = date.ToDateTime(new TimeOnly(time.Hour, time.Minute), DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
            throw new BusinessException(ErrorCodes.LocalTimeNonexistent,
                $"{local:yyyy-MM-dd HH:mm} does not exist in {zone.Id} (the clocks skip it for daylight saving). Choose another time.");

        int offset;
        if (zone.IsAmbiguousTime(local))
        {
            var candidates = zone.GetAmbiguousTimeOffsets(local).Select(o => (int)o.TotalMinutes).ToList();
            if (chosenOffsetMinutes is not { } chosen || !candidates.Contains(chosen))
                throw new BusinessException(ErrorCodes.LocalTimeAmbiguous,
                    $"{local:yyyy-MM-dd HH:mm} occurs twice in {zone.Id} (daylight saving ends). Choose which occurrence (UTC offset) you mean.");
            offset = chosen;
        }
        else
        {
            offset = (int)zone.GetUtcOffset(local).TotalMinutes;
        }

        var utc = DateTime.SpecifyKind(local.AddMinutes(-offset), DateTimeKind.Utc);
        if (utc < utcNow + MinimumLead)
            throw Guard.Invalid("Publish time", "must be at least one minute in the future; use Publish Now to publish immediately");
        if (utc > utcNow + MaximumHorizon)
            throw Guard.Invalid("Publish time", "must be within one year");
        return new PublishTime(utc, local, zone.Id, offset);
    }
}

/// <summary>
/// One lifetime publication intent for one exact approved PostVersion and one SocialAccount (PRD §27, ARCHITECTURE.md §25).
/// It freezes the approved payload, its hash and the destination's execution mode at creation, so later changes to the
/// account never rewrite delivery history. Delivery itself happens only in the publishing worker.
/// </summary>
public sealed class PublicationJob
{
    public const int DefaultMaxAttempts = 3;
    public const int ErrorMessageMax = 1000;

    private PublicationJob() { }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid PostId { get; private set; }
    public Guid PostVersionId { get; private set; }

    /// <summary>The approval (an APPROVED ReviewRequest of exactly this version) that authorizes this intent.</summary>
    public Guid ReviewRequestId { get; private set; }

    public Guid SocialAccountId { get; private set; }
    public SocialPlatform Platform { get; private set; }

    /// <summary>Snapshot of the destination's mode when the intent was created; SIMULATED can never end PUBLISHED.</summary>
    public ExecutionMode ExecutionMode { get; private set; }

    public string PayloadVersion { get; private set; } = null!;
    public string PayloadJson { get; private set; } = null!;
    public string PayloadHash { get; private set; } = null!;

    public DateTime PublishAtUtc { get; private set; }
    public DateTime? LocalPublishAt { get; private set; }
    public string? TimeZoneId { get; private set; }
    public int? UtcOffsetMinutes { get; private set; }

    public DeliveryState DeliveryState { get; private set; }
    public int AttemptCount { get; private set; }
    public int MaxAttempts { get; private set; }
    public DateTime? NextActionAtUtc { get; private set; }

    // Worker lease and dispatch checkpoints (written by the publishing worker, Phase 14+).
    public string? LockedBy { get; private set; }
    public DateTime? LockedAtUtc { get; private set; }
    public DateTime? LeaseExpiresAtUtc { get; private set; }
    public Guid? LeaseToken { get; private set; }
    public DateTime? DispatchStartedAtUtc { get; private set; }
    public string? ProviderContainerId { get; private set; }

    public string? ExternalPostId { get; private set; }
    public string? ExternalPostUrl { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public string? LastErrorCode { get; private set; }
    public string? LastErrorMessage { get; private set; }

    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CanceledByUserId { get; private set; }
    public DateTime? CanceledAtUtc { get; private set; }
    /// <summary>Traces the request that created this intent across API, worker, provider calls and logs.</summary>
    public string? CorrelationId { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    /// <summary>States in which external dispatch may have begun; nothing but the worker may touch these.</summary>
    public bool IsInFlight => DeliveryState is DeliveryState.PROCESSING or DeliveryState.WAITING_MEDIA or DeliveryState.RECONCILIATION_REQUIRED;

    /// <summary>No external side effect has started, so the intent can still be withdrawn.</summary>
    public bool CanCancel => DeliveryState is DeliveryState.SCHEDULED or DeliveryState.RETRY_PENDING or DeliveryState.ACTION_REQUIRED;

    /// <summary>
    /// ACTION_REQUIRED is included: it means the provider definitively refused (nothing was published), so after the
    /// cause is fixed the same intent may go out again. Uncertain outcomes are RECONCILIATION_REQUIRED and stay excluded.
    /// </summary>
    public bool CanReschedule => DeliveryState is DeliveryState.SCHEDULED or DeliveryState.CANCELED or DeliveryState.ACTION_REQUIRED;

    /// <summary>
    /// Creates the intent after re-verifying, on the exact objects, that the post is APPROVED, that the active approval
    /// covers its current version, that the destination is the approved one and can receive content, and that the
    /// payload rebuilt from stored data still hashes to what the reviewer approved.
    /// </summary>
    public static PublicationJob Create(
        Guid id, Post post, PostVersion version, ReviewRequest approval, SocialAccount account, ApprovalPayload payload,
        PublishTime time, Guid userId, DateTime utcNow)
    {
        EnsureAuthorized(post, version, approval, account, payload);
        var job = new PublicationJob
        {
            Id = id,
            OrganizationId = post.OrganizationId,
            PostId = post.Id,
            PostVersionId = version.Id,
            ReviewRequestId = approval.Id,
            SocialAccountId = account.Id,
            Platform = post.Platform,
            ExecutionMode = account.ExecutionMode,
            PayloadVersion = payload.PayloadVersion,
            PayloadJson = payload.ToCanonicalJson(),
            PayloadHash = approval.ApprovedPayloadHash!,
            MaxAttempts = DefaultMaxAttempts,
            CreatedByUserId = userId,
            CreatedAtUtc = utcNow,
        };
        job.SetSchedule(time, utcNow);
        return job;
    }

    /// <summary>Same checks as creation, used before an existing intent is moved or reactivated.</summary>
    public static void EnsureAuthorized(Post post, PostVersion version, ReviewRequest approval, SocialAccount account, ApprovalPayload payload)
    {
        if (post.EditorialState != EditorialState.APPROVED || post.ActiveApprovalId is null)
            throw new BusinessException(ErrorCodes.ApprovalRequired, $"Only approved content can be scheduled; this post is {post.EditorialState}.");
        if (approval.Id != post.ActiveApprovalId || approval.Status != ReviewStatus.APPROVED || approval.ApprovedPayloadHash is null
            || approval.PostId != post.Id || approval.PostVersionId != version.Id || version.Id != post.CurrentVersionId)
            throw new BusinessException(ErrorCodes.ApprovalStale, "The approval no longer covers the post's current version. Reload the post.");
        if (account.Id != version.SocialAccountId || account.OrganizationId != post.OrganizationId)
            throw new BusinessException(ErrorCodes.ApprovalStale, "The destination differs from the approved one.");
        if (account.ExecutionMode == ExecutionMode.NOT_CONFIGURED || !account.IsEligibleDestination)
            throw new BusinessException(ErrorCodes.SocialAccountNotReady,
                $"The destination \"{account.DisplayName}\" is {account.ExecutionMode}/{account.ConnectionStatus} and cannot publish.");
        if (!payload.Matches(approval.ApprovedPayloadHash))
            throw new BusinessException(ErrorCodes.ApprovalStale, "The content no longer matches the approved payload; it must be reviewed again.");
    }

    /// <summary>Moves a not-yet-dispatched intent, or reactivates a canceled one (still the same lifetime intent).</summary>
    public void Reschedule(PublishTime time, DateTime utcNow)
    {
        if (!CanReschedule)
            throw new BusinessException(IsInFlight ? ErrorCodes.PublicationTooLateToCancel : ErrorCodes.InvalidStateTransition,
                $"A publication in {DeliveryState} cannot be rescheduled.");
        CanceledByUserId = null;
        CanceledAtUtc = null;
        if (DeliveryState == DeliveryState.ACTION_REQUIRED)
        {
            // A fresh try after a refused one: start before dispatch again with a full retry budget (attempt rows keep history).
            // Any Instagram container from the refused try may have expired, so a new one is created.
            DispatchStartedAtUtc = null;
            ProviderContainerId = null;
            MediaContainerCreatedAtUtc = null;
            AttemptCount = 0;
        }
        SetSchedule(time, utcNow);
    }

    public void Cancel(Guid userId, DateTime utcNow)
    {
        if (!CanCancel)
            throw new BusinessException(IsInFlight ? ErrorCodes.PublicationTooLateToCancel : ErrorCodes.InvalidStateTransition,
                IsInFlight
                    ? $"Dispatch has already started ({DeliveryState}); the publication can no longer be canceled safely."
                    : $"A publication in {DeliveryState} cannot be canceled.");
        DeliveryState = DeliveryState.CANCELED;
        NextActionAtUtc = null;
        CanceledByUserId = userId;
        CanceledAtUtc = utcNow;
        UpdatedAtUtc = utcNow;
    }

    public void AttachCorrelation(string? correlationId) => CorrelationId ??= CorrelationIds.Normalize(correlationId);

    /// <summary>
    /// Manual reconciliation by a platform operator after checking the destination. Only an uncertain outcome can be
    /// resolved, and a simulated job can never be confirmed as published. Nothing is sent again from here.
    /// </summary>
    public void ConfirmPublished(string? externalPostId, string? externalPostUrl, DateTime utcNow)
    {
        EnsureReconciling();
        if (ExecutionMode == ExecutionMode.SIMULATED)
            throw new BusinessException(ErrorCodes.InvalidStateTransition, "A simulated publication can never be confirmed as published.");
        ExternalPostId = Guard.Optional(externalPostId, "External post id", 200) ?? ExternalPostId;
        ExternalPostUrl = Guard.OptionalHttpUrl(externalPostUrl, "External post URL") ?? ExternalPostUrl;
        Finish(DeliveryState.PUBLISHED, null, null, utcNow);
    }

    public void ConfirmFailed(string reason, DateTime utcNow)
    {
        EnsureReconciling();
        Finish(DeliveryState.FAILED, PublicationErrorCodes.ManuallyReconciledFailed, Guard.Required(reason, "Reason", 500), utcNow);
    }

    private void EnsureReconciling()
    {
        if (DeliveryState != DeliveryState.RECONCILIATION_REQUIRED)
            throw new BusinessException(ErrorCodes.InvalidStateTransition, "Only a publication that requires reconciliation can be resolved manually.");
    }

    public bool IsSameSchedule(PublishTime time) =>
        PublishAtUtc == time.Utc && LocalPublishAt == time.Local && TimeZoneId == time.TimeZoneId;

    // ---- Worker lifecycle (every transition is fenced by the lease token) --------------------------------

    /// <summary>A job this late at its first dispatch is not sent automatically; a human decides (late-job policy).</summary>
    public static readonly TimeSpan MaxLateness = TimeSpan.FromHours(2);

    /// <summary>How long a provider may take to process uploaded media before a human is asked to look.</summary>
    public static readonly TimeSpan MaxMediaWait = TimeSpan.FromMinutes(30);

    public DateTime? MediaContainerCreatedAtUtc { get; private set; }

    public bool IsLate(DateTime utcNow) => DispatchStartedAtUtc is null && ProviderContainerId is null && utcNow - PublishAtUtc > MaxLateness;

    public bool MediaWaitExpired(DateTime utcNow) => MediaContainerCreatedAtUtc is { } created && utcNow - created > MaxMediaWait;

    public void EnsureLease(Guid leaseToken)
    {
        if (DeliveryState != DeliveryState.PROCESSING || LeaseToken != leaseToken)
            throw new BusinessException(ErrorCodes.ConcurrencyConflict, "The publication lease is no longer held by this worker.");
    }

    /// <summary>Checkpoint: the provider-side media container exists, so a restart checks it instead of creating another.</summary>
    public void RecordMediaContainer(Guid leaseToken, string containerId, DateTime utcNow)
    {
        EnsureLease(leaseToken);
        ProviderContainerId = Guard.Required(containerId, "Provider container id", 200);
        MediaContainerCreatedAtUtc = utcNow;
        UpdatedAtUtc = utcNow;
    }

    public void WaitForMedia(Guid leaseToken, DateTime nextCheckAtUtc, DateTime utcNow)
    {
        EnsureLease(leaseToken);
        DeliveryState = DeliveryState.WAITING_MEDIA;
        NextActionAtUtc = nextCheckAtUtc;
        ReleaseLease(utcNow);
    }

    /// <summary>
    /// Checkpoint saved before the publishing call. From here on a lost lease or unknown outcome means the post may exist,
    /// so the job is reconciled by a human instead of being sent again.
    /// </summary>
    public void MarkDispatchStarted(Guid leaseToken, DateTime utcNow)
    {
        EnsureLease(leaseToken);
        DispatchStartedAtUtc = utcNow;
        UpdatedAtUtc = utcNow;
    }

    public void CompletePublished(Guid leaseToken, string? externalPostId, string? externalPostUrl, DateTime utcNow)
    {
        EnsureLease(leaseToken);
        if (ExecutionMode == ExecutionMode.SIMULATED)
            throw new InvalidOperationException("A simulated job can never be reported as PUBLISHED.");
        ExternalPostId = externalPostId;
        ExternalPostUrl = externalPostUrl;
        Finish(DeliveryState.PUBLISHED, null, null, utcNow);
    }

    public void CompleteSimulated(Guid leaseToken, DateTime utcNow)
    {
        EnsureLease(leaseToken);
        if (ExecutionMode != ExecutionMode.SIMULATED)
            throw new InvalidOperationException("Only a job created for a SIMULATED destination can end as SIMULATED.");
        Finish(DeliveryState.SIMULATED, null, null, utcNow);
    }

    /// <summary>Known-safe failure: back off and try again while attempts remain, otherwise fail permanently.</summary>
    public void ScheduleRetry(Guid leaseToken, string code, string message, DateTime utcNow)
    {
        EnsureLease(leaseToken);
        if (AttemptCount >= MaxAttempts)
        {
            Finish(DeliveryState.FAILED, code, $"{message} (gave up after {AttemptCount} attempts)", utcNow);
            return;
        }
        SetError(code, message);
        DeliveryState = DeliveryState.RETRY_PENDING;
        // The failure was established as safe (nothing was created), so the next attempt starts before dispatch again.
        DispatchStartedAtUtc = null;
        NextActionAtUtc = utcNow.Add(RetryDelay(AttemptCount));
        ReleaseLease(utcNow);
    }

    public void RequireAction(Guid leaseToken, string code, string message, DateTime utcNow)
    {
        EnsureLease(leaseToken);
        Finish(DeliveryState.ACTION_REQUIRED, code, message, utcNow, completed: false);
    }

    /// <summary>The post may or may not exist on the provider. Never retried automatically.</summary>
    public void RequireReconciliation(Guid leaseToken, string code, string message, string? evidenceExternalId, DateTime utcNow)
    {
        EnsureLease(leaseToken);
        if (ExecutionMode != ExecutionMode.SIMULATED)
            ExternalPostId = evidenceExternalId ?? ExternalPostId;
        Finish(DeliveryState.RECONCILIATION_REQUIRED, code, message, utcNow, completed: false);
    }

    public void Fail(Guid leaseToken, string code, string message, DateTime utcNow)
    {
        EnsureLease(leaseToken);
        Finish(DeliveryState.FAILED, code, message, utcNow);
    }

    /// <summary>
    /// Recovery for a worker that died after the dispatch checkpoint of a real send: the outcome is unknown.
    /// Simulated jobs have no external side effect, so they are simply reclaimed instead.
    /// </summary>
    public bool ReconcileAbandoned(DateTime utcNow)
    {
        if (DeliveryState != DeliveryState.PROCESSING || LeaseExpiresAtUtc >= utcNow || DispatchStartedAtUtc is null
            || ExecutionMode == ExecutionMode.SIMULATED)
            return false;
        Finish(DeliveryState.RECONCILIATION_REQUIRED, PublicationErrorCodes.LeaseLostAfterDispatch,
            "The worker stopped after sending to the provider; whether the post was created is unknown. Check the destination.", utcNow, completed: false);
        return true;
    }

    /// <summary>A worker died before sending on its final attempt.</summary>
    public bool FailAbandoned(DateTime utcNow)
    {
        if (DeliveryState != DeliveryState.PROCESSING || LeaseExpiresAtUtc >= utcNow || AttemptCount < MaxAttempts || ProviderContainerId is not null
            || (DispatchStartedAtUtc is not null && ExecutionMode != ExecutionMode.SIMULATED))
            return false;
        Finish(DeliveryState.FAILED, PublicationErrorCodes.WorkerLost, "The worker stopped responding on the final attempt.", utcNow);
        return true;
    }

    private static TimeSpan RetryDelay(int attempt) => TimeSpan.FromMinutes(attempt switch { <= 1 => 1, 2 => 5, _ => 15 });

    private void Finish(DeliveryState state, string? code, string? message, DateTime utcNow, bool completed = true)
    {
        DeliveryState = state;
        if (code is not null)
            SetError(code, message ?? code);
        NextActionAtUtc = null;
        if (completed)
            CompletedAtUtc = utcNow;
        ReleaseLease(utcNow);
    }

    private void SetError(string code, string message)
    {
        LastErrorCode = code.Length > 100 ? code[..100] : code;
        LastErrorMessage = message.Length > ErrorMessageMax ? message[..ErrorMessageMax] : message;
    }

    private void ReleaseLease(DateTime utcNow)
    {
        LockedBy = null;
        LockedAtUtc = null;
        LeaseExpiresAtUtc = null;
        LeaseToken = null;
        UpdatedAtUtc = utcNow;
    }

    private void SetSchedule(PublishTime time, DateTime utcNow)
    {
        PublishAtUtc = time.Utc;
        LocalPublishAt = time.Local;
        TimeZoneId = time.TimeZoneId;
        UtcOffsetMinutes = time.UtcOffsetMinutes;
        DeliveryState = DeliveryState.SCHEDULED;
        NextActionAtUtc = time.Utc;
        UpdatedAtUtc = utcNow;
    }
}
