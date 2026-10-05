using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Application.Organizations.DTOs;

public sealed record OrganizationDto(
    Guid Id,
    string Name,
    string Industry,
    string DefaultLanguage,
    string TimeZoneId,
    bool RequireDistinctReviewer,
    OrganizationStatus Status,
    OrganizationRole MyRole,
    byte[] RowVersion)
{
    public static OrganizationDto From(Organization organization, OrganizationRole myRole) => new(
        organization.Id, organization.Name, organization.Industry, organization.DefaultLanguage, organization.TimeZoneId,
        organization.RequireDistinctReviewer, organization.Status, myRole, organization.RowVersion);
}

public sealed record MembershipSummaryDto(Guid OrganizationId, string OrganizationName, OrganizationRole Role);

public sealed record MemberDto(
    Guid MemberId,
    Guid UserId,
    string Email,
    string DisplayName,
    OrganizationRole Role,
    MembershipStatus Status,
    byte[] RowVersion);
