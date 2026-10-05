namespace SocialMediaAutomation.Domain.Entities;

/// <summary>How a provider failure must be handled (ARCHITECTURE.md §34). Only RETRYABLE is ever retried automatically.</summary>
public enum PublishErrorCategory
{
    /// <summary>Established that nothing was created (pre-send network failure, documented throttling).</summary>
    RETRYABLE,

    /// <summary>A human must fix something (auth, permission, account, media, lateness).</summary>
    ACTION_REQUIRED,

    /// <summary>The post may exist. Reconcile; never resend blindly.</summary>
    UNCERTAIN,

    /// <summary>Will not succeed as is and needs no reconciliation.</summary>
    PERMANENT,
}

public enum PublicationAttemptStep
{
    /// <summary>Revalidation, preparation and (if reached) the publishing call.</summary>
    DISPATCH,

    /// <summary>Checking provider-side media processing of an existing container.</summary>
    MEDIA_STATUS,
}

/// <summary>Stable codes for publication outcomes recorded on jobs and attempts.</summary>
public static class PublicationErrorCodes
{
    public const string LeaseLostAfterDispatch = "LEASE_LOST_AFTER_DISPATCH";
    public const string ManuallyReconciledFailed = "MANUALLY_RECONCILED_FAILED";
    public const string WorkerLost = "WORKER_LOST";
    public const string PersistenceFailedAfterSend = "PERSISTENCE_FAILED_AFTER_SEND";
    public const string UnexpectedAfterSend = "UNEXPECTED_ERROR_AFTER_SEND";
    public const string UnexpectedBeforeSend = "UNEXPECTED_ERROR_BEFORE_SEND";
    public const string TooLate = "PUBLICATION_TOO_LATE";
    public const string MediaProcessingTimeout = "MEDIA_PROCESSING_TIMEOUT";
    public const string MediaProcessingFailed = "MEDIA_PROCESSING_FAILED";
    public const string NoPublisher = "NO_PUBLISHER";
    public const string WorkspaceInactive = "WORKSPACE_INACTIVE";
    public const string MediaMissing = "MEDIA_MISSING";
    public const string MediaInvalid = "MEDIA_INVALID";
    public const string AuthExpired = "AUTH_EXPIRED";
    public const string AuthRevoked = "AUTH_REVOKED";
    public const string NoExternalId = "PROVIDER_RETURNED_NO_ID";
}

/// <summary>
/// One worker pass over a publication job: what it did, how it ended and the provider evidence it saw. Rows are
/// inserted when the pass starts (before any provider call) and finished once, so a crash leaves a visible open attempt.
/// </summary>
public sealed class PublicationAttempt
{
    public const int DetailMax = 1000;

    private PublicationAttempt() { }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid PublicationJobId { get; private set; }
    public int AttemptNumber { get; private set; }
    public PublicationAttemptStep Step { get; private set; }
    public string WorkerId { get; private set; } = null!;
    public DateTime StartedAtUtc { get; private set; }
    public DateTime? FinishedAtUtc { get; private set; }

    /// <summary>The job's delivery state when this pass ended; null while running (or if the worker died).</summary>
    public DeliveryState? ResultState { get; private set; }

    public PublishErrorCategory? ErrorCategory { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? ProviderRequestId { get; private set; }
    public string? ExternalId { get; private set; }
    public string? Detail { get; private set; }

    public static PublicationAttempt Start(PublicationJob job, PublicationAttemptStep step, string workerId, DateTime utcNow) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = job.OrganizationId,
        PublicationJobId = job.Id,
        AttemptNumber = job.AttemptCount,
        Step = step,
        WorkerId = workerId.Length > 200 ? workerId[..200] : workerId,
        StartedAtUtc = utcNow,
    };

    public void Finish(DeliveryState resultState, DateTime utcNow, PublishErrorCategory? category = null, string? code = null, string? message = null,
        string? providerRequestId = null, string? externalId = null, string? detail = null)
    {
        ResultState = resultState;
        FinishedAtUtc = utcNow;
        ErrorCategory = category;
        ErrorCode = Cut(code, 100);
        ErrorMessage = Cut(message, PublicationJob.ErrorMessageMax);
        ProviderRequestId = Cut(providerRequestId, 200) ?? ProviderRequestId;
        ExternalId = Cut(externalId, 200) ?? ExternalId;
        Detail = Cut(detail, DetailMax);
    }

    private static string? Cut(string? value, int max) => value is null ? null : value.Length > max ? value[..max] : value;
}
