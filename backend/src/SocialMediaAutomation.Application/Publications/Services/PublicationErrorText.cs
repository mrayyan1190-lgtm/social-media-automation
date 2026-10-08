using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Application.Publications.Services;

/// <summary>
/// What a workspace user reads about a publishing problem. Provider and infrastructure details (raw API errors, HTTP
/// statuses, exception names) stay on the job and attempt rows and in the admin logs; customers get a plain, actionable
/// sentence. Messages the application writes itself about things the user controls are kept as they are.
/// </summary>
public static class PublicationErrorText
{
    public static string? ForCustomer(SocialPlatform platform, string? code, string? message) => code switch
    {
        null => message,
        // Written by the application and actionable by the user.
        PublicationErrorCodes.TooLate or PublicationErrorCodes.WorkspaceInactive or ErrorCodes.ApprovalStale
            or ErrorCodes.SocialAccountNotReady or PublicationErrorCodes.ManuallyReconciledFailed => message,
        PublicationErrorCodes.AuthExpired or PublicationErrorCodes.AuthRevoked or "AUTH_INVALID" =>
            $"The {platform} connection expired or was revoked. Reconnect the account, then publish again.",
        "PERMISSION_MISSING" or "CAPABILITY_MISSING" or "ACCOUNT_INVALID" =>
            $"The connected {platform} account is missing a permission. Reconnect it with the requested permissions.",
        PublicationErrorCodes.MediaMissing or PublicationErrorCodes.MediaInvalid or PublicationErrorCodes.MediaProcessingFailed
            or PublicationErrorCodes.MediaProcessingTimeout or "MEDIA_URL_UNAVAILABLE" or "MEDIA_READ_FAILED" =>
            $"{platform} could not use the post's images. Replace them (a new version) or try again later.",
        PublicationErrorCodes.NoExternalId or PublicationErrorCodes.PersistenceFailedAfterSend or PublicationErrorCodes.UnexpectedAfterSend
            or PublicationErrorCodes.LeaseLostAfterDispatch or PublicationErrorCodes.WorkerLost
            or "TIMEOUT_AFTER_SEND" or "PROVIDER_ERROR_AFTER_SEND" or "UNREADABLE_RESPONSE" =>
            $"We could not confirm whether the post appeared on {platform}. Please check {platform}; our team has been notified.",
        "THROTTLED" or "NETWORK_ERROR" or "NETWORK_BEFORE_SEND" or "PROVIDER_UNAVAILABLE" or "UNEXPECTED_PROVIDER_ERROR"
            or PublicationErrorCodes.UnexpectedBeforeSend =>
            $"{platform} is temporarily unavailable. The post will be retried automatically.",
        _ => $"{platform} could not publish this post. Our team has been notified and is looking into it.",
    };
}
