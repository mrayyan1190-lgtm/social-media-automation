using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Application.BrandProfiles.DTOs;

public sealed record BrandProfileDto(
    Guid Id,
    Guid OrganizationId,
    string BusinessDescription,
    string TargetAudience,
    BrandTone BrandTone,
    string? Website,
    IReadOnlyList<string> ProhibitedTerms,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    byte[] RowVersion)
{
    public static BrandProfileDto From(BrandProfile p) => new(
        p.Id, p.OrganizationId, p.BusinessDescription, p.TargetAudience, p.BrandTone, p.Website, p.ProhibitedTerms,
        p.CreatedAtUtc, p.UpdatedAtUtc, p.RowVersion);
}
