using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Application.Campaigns.DTOs;

/// <summary>Campaign list row: the folder plus how many (non-archived) posts it holds.</summary>
public sealed record CampaignSummaryDto(
    Guid Id,
    string Name,
    string Color,
    string? Description,
    CampaignStatus Status,
    int PostCount,
    DateTime UpdatedAtUtc,
    Guid CreatedByUserId);

public sealed record CampaignDto(
    Guid Id,
    Guid OrganizationId,
    string Name,
    string Color,
    string? Description,
    CampaignStatus Status,
    Guid CreatedByUserId,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    byte[] RowVersion)
{
    public static CampaignDto From(Campaign c) => new(
        c.Id, c.OrganizationId, c.Name, c.Color, c.Description, c.Status, c.CreatedByUserId, c.CreatedAtUtc, c.UpdatedAtUtc, c.RowVersion);
}
