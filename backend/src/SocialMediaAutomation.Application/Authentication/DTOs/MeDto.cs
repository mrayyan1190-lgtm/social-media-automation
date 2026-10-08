using SocialMediaAutomation.Application.Organizations.DTOs;

namespace SocialMediaAutomation.Application.Authentication.DTOs;

/// <summary>The signed-in user's session context; also the login response. Never carries credentials.</summary>
public sealed record MeDto(
    Guid UserId, string Email, string DisplayName, IReadOnlyList<MembershipSummaryDto> Memberships, OnboardingStatusDto Onboarding, bool IsSystemAdmin);

/// <summary>GET /api/internal/me: the signed-in system administrator.</summary>
public sealed record SystemAdminMeDto(Guid Id, string FullName, string Email, string SystemRole);

public sealed record RegisteredUserDto(Guid Id, string Email, Guid OrganizationId);

/// <summary>Where the user should go next, decided by the backend so the frontend never guesses (PRD §9).</summary>
public sealed record OnboardingStatusDto(
    Guid? OrganizationId,
    bool AccountCreated,
    bool WorkspaceCreated,
    bool ProfileCompleted,
    bool BrandProfileCompleted,
    bool SocialAccountsConnected,
    bool CanCreateContent,
    string NextStep);

/// <summary>
/// New-user onboarding order: profile, brand profile, social connections, then the dashboard (sign-up creates the
/// workspace). WORKSPACE only applies to an account left without any workspace.
/// </summary>
public static class OnboardingSteps
{
    public const string Workspace = "WORKSPACE";
    public const string Profile = "PROFILE";
    public const string BrandProfile = "BRAND_PROFILE";
    public const string SocialConnections = "SOCIAL_CONNECTIONS";
    public const string Complete = "COMPLETE";
}
