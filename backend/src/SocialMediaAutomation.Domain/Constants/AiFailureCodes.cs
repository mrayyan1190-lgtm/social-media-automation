namespace SocialMediaAutomation.Domain.Constants;

/// <summary>Stable failure codes recorded on failed AI requests and shown to users.</summary>
public static class AiFailureCodes
{
    public const string NotConfigured = "AI_NOT_CONFIGURED";
    public const string ProviderAuth = "AI_PROVIDER_AUTH";
    public const string ProviderUnavailable = "AI_PROVIDER_UNAVAILABLE";
    public const string ProviderRejected = "AI_PROVIDER_REJECTED";
    public const string OutputInvalid = "AI_OUTPUT_INVALID";
    public const string SourceChanged = "AI_SOURCE_CHANGED";
    public const string TargetNotEditable = "AI_TARGET_NOT_EDITABLE";
    public const string WorkerLost = "AI_WORKER_LOST";
    public const string ImageNotSuitable = "AI_IMAGE_NOT_SUITABLE";
    /// <summary>An unexpected error inside the worker (a bug, not the provider); details are in the worker log.</summary>
    public const string Unexpected = "AI_UNEXPECTED_ERROR";
}
