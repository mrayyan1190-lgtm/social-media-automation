namespace SocialMediaAutomation.Domain.Constants;

/// <summary>Platform-level (not workspace) authorization. OrganizationMember roles stay Admin, Creator and Reviewer.</summary>
public static class SystemRoles
{
    public const string SystemAdmin = "SystemAdmin";

    /// <summary>Session claim added only by the internal sign-in, so the internal area needs that explicit sign-in.</summary>
    public const string ClaimType = "system_role";

    /// <summary>ASP.NET Core authorization policy for every internal endpoint.</summary>
    public const string RequireSystemAdminPolicy = "RequireSystemAdmin";
}
