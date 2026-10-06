namespace SocialMediaAutomation.Domain.Constants;

/// <summary>Stable machine-readable error codes returned by the API (ARCHITECTURE.md §41). Never rename existing values.</summary>
public static class ErrorCodes
{
    public const string Unauthorized = "UNAUTHORIZED";
    /// <summary>An unexpected server failure; the response carries only a generic message and the correlation id.</summary>
    public const string InternalError = "INTERNAL_ERROR";
    /// <summary>The request itself was not acceptable (unknown route, wrong method or media type).</summary>
    public const string RequestInvalid = "REQUEST_INVALID";
    public const string Forbidden = "FORBIDDEN";
    public const string WorkspaceSuspended = "WORKSPACE_SUSPENDED";
    public const string AccountDisabled = "ACCOUNT_DISABLED";
    public const string NotFound = "NOT_FOUND";
    public const string TenantMismatch = "TENANT_MISMATCH";
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";
    public const string InvalidStateTransition = "INVALID_STATE_TRANSITION";
    public const string AlreadyExists = "ALREADY_EXISTS";
    public const string AiNotConfigured = "AI_NOT_CONFIGURED";
    public const string AiGenerationFailed = "AI_GENERATION_FAILED";
    public const string SubmissionBlocked = "SUBMISSION_BLOCKED";
    public const string ApprovalStale = "APPROVAL_STALE";
    public const string DistinctReviewerRequired = "DISTINCT_REVIEWER_REQUIRED";
    public const string OAuthStateInvalid = "OAUTH_STATE_INVALID";
    public const string ProviderNotConfigured = "PROVIDER_NOT_CONFIGURED";
    public const string SocialAccountNotReady = "SOCIAL_ACCOUNT_NOT_READY";
    public const string ApprovalRequired = "APPROVAL_REQUIRED";
    public const string IdempotencyConflict = "IDEMPOTENCY_CONFLICT";
    public const string PublicationTooLateToCancel = "PUBLICATION_TOO_LATE_TO_CANCEL";
    public const string LocalTimeNonexistent = "LOCAL_TIME_NONEXISTENT";
    public const string LocalTimeAmbiguous = "LOCAL_TIME_AMBIGUOUS";
    public const string EmailAlreadyRegistered = "EMAIL_ALREADY_REGISTERED";
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string InvalidTimeZone = "INVALID_TIMEZONE";
    public const string InvalidCampaignObjective = "INVALID_CAMPAIGN_OBJECTIVE";
    public const string InvalidPlatform = "INVALID_PLATFORM";
    public const string CampaignNotReadyForGeneration = "CAMPAIGN_NOT_READY_FOR_GENERATION";
}
