namespace SocialMediaAutomation.Domain.Enums;

/// <summary>Server-side permissions checked per organization.</summary>
public enum Permission
{
    ViewOrganization,
    ManageOrganizationSettings,
    ViewMembers,
    ManageMembers,
    ViewBrandProfile,
    ManageBrandProfile,
    ViewContent,
    ManageContent,
    ReviewContent,
    CommentOnReviews,
    ViewSocialAccounts,
    ManageSocialAccounts,
    ViewAuditLog,
    /// <summary>Schedule, Publish Now, reschedule and cancel an already approved version (PRD §7).</summary>
    SchedulePublication,
}
