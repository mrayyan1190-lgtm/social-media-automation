using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Domain.Common;

/// <summary>The single source of truth for which role holds which permission (PRD §7).</summary>
public static class RolePermissions
{
    private static readonly Dictionary<OrganizationRole, HashSet<Permission>> Grants = new()
    {
        [OrganizationRole.Admin] = [.. Enum.GetValues<Permission>()],
        // Creators author content and submit it for review; they never decide reviews, schedule or publish (MVP: the Admin
        // approves and publishes).
        [OrganizationRole.Creator] =
        [
            Permission.ViewOrganization, Permission.ViewMembers, Permission.ViewBrandProfile, Permission.ViewContent,
            Permission.ManageContent, Permission.CommentOnReviews, Permission.ViewSocialAccounts,
        ],
        // Reviewers inspect exact versions and decide; they do not author or edit content (PRD §7.3).
        [OrganizationRole.Reviewer] =
        [
            Permission.ViewOrganization, Permission.ViewMembers, Permission.ViewBrandProfile, Permission.ViewContent,
            Permission.ReviewContent, Permission.CommentOnReviews, Permission.ViewSocialAccounts,
        ],
    };

    public static bool Has(OrganizationRole role, Permission permission) => Grants[role].Contains(permission);
}
