namespace SocialMediaAutomation.Domain.Constants;

/// <summary>Stable audit action names (PRD §32). Never rename existing values; history depends on them.</summary>
public static class AuditActions
{
    public const string WorkspaceCreated = "WORKSPACE_CREATED";
    public const string BrandProfileSaved = "BRAND_PROFILE_SAVED";
    public const string CampaignCreated = "CAMPAIGN_CREATED";
    public const string CampaignUpdated = "CAMPAIGN_UPDATED";
    public const string PostVersionCreated = "POST_VERSION_CREATED";
    public const string PostCampaignChanged = "POST_CAMPAIGN_CHANGED";
    public const string ReviewSubmitted = "REVIEW_SUBMITTED";
    public const string ChangesRequested = "CHANGES_REQUESTED";
    public const string ContentApproved = "CONTENT_APPROVED";
    public const string ApprovalRevoked = "APPROVAL_REVOKED";
    public const string ReviewCommented = "REVIEW_COMMENTED";
    public const string SocialAccountCreated = "SOCIAL_ACCOUNT_CREATED";
    public const string SocialConnectionConnected = "SOCIAL_CONNECTION_CONNECTED";
    public const string SocialConnectionDisconnected = "SOCIAL_CONNECTION_DISCONNECTED";
    public const string PublicationScheduled = "PUBLICATION_SCHEDULED";
    public const string PublicationRescheduled = "PUBLICATION_RESCHEDULED";
    public const string PublicationCanceled = "PUBLICATION_CANCELED";
    public const string PublicationPublished = "PUBLICATION_PUBLISHED";
    public const string PublicationSimulated = "PUBLICATION_SIMULATED";
    public const string PublicationFailed = "PUBLICATION_FAILED";
    public const string PublicationActionRequired = "PUBLICATION_ACTION_REQUIRED";
    public const string PublicationReconciliationRequired = "PUBLICATION_RECONCILIATION_REQUIRED";
    public const string MemberAdded = "MEMBER_ADDED";
    public const string MemberDeactivated = "MEMBER_DEACTIVATED";
}
