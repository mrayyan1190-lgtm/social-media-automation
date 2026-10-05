using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Application.Organizations.Commands;

public sealed record CreateOrganizationCommand(string Name, string Industry, string? DefaultLanguage, string TimeZoneId, bool RequireDistinctReviewer);

public sealed record UpdateOrganizationSettingsCommand(
    Guid OrganizationId, string Name, string Industry, string? DefaultLanguage, string TimeZoneId, bool RequireDistinctReviewer, byte[]? ExpectedRowVersion);

public sealed record ChangeMemberRoleCommand(Guid OrganizationId, Guid MemberId, OrganizationRole Role, byte[]? ExpectedRowVersion);

public sealed record AddMemberCommand(Guid OrganizationId, string Email, OrganizationRole Role);

public sealed record DeactivateMemberCommand(Guid OrganizationId, Guid MemberId, byte[]? ExpectedRowVersion);
