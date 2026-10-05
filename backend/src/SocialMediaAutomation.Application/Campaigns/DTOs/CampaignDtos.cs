using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Application.Campaigns.DTOs;

public sealed record CampaignSummaryDto(
    Guid Id,
    string Name,
    CampaignObjective Objective,
    IReadOnlyList<SocialPlatform> Platforms,
    CampaignStatus Status,
    int FactCount,
    DateTime UpdatedAtUtc);

public sealed record CampaignFactDto(
    Guid Id,
    Guid CampaignId,
    string Text,
    string? SourceNote,
    Guid SuppliedByUserId,
    string SuppliedByName,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    byte[] RowVersion);

public sealed record CampaignDto(
    Guid Id,
    Guid OrganizationId,
    string Name,
    CampaignObjective Objective,
    string Description,
    string TargetAudience,
    CallToActionType CallToAction,
    string? CallToActionUrl,
    BrandTone? ToneOverride,
    ContentLength ContentLength,
    HashtagMode HashtagMode,
    EmojiMode EmojiMode,
    IReadOnlyList<SocialPlatform> Platforms,
    CampaignStatus Status,
    Guid CreatedByUserId,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    byte[] RowVersion,
    IReadOnlyList<CampaignFactDto> Facts)
{
    public static CampaignDto From(Campaign c, IReadOnlyList<CampaignFactDto> facts) => new(
        c.Id, c.OrganizationId, c.Name, c.Objective, c.Description, c.TargetAudience, c.CallToAction, c.CallToActionUrl,
        c.ToneOverride, c.ContentLength, c.HashtagMode, c.EmojiMode, c.Platforms, c.Status, c.CreatedByUserId, c.CreatedAtUtc, c.UpdatedAtUtc,
        c.RowVersion, facts);
}

// ---- Resolved campaign brief: what AI generation will receive, and whether it can start -------------------------

public sealed record CampaignBriefDto(
    BriefCampaignDto Campaign,
    BriefBrandContextDto? BrandContext,
    BriefCallToActionDto CallToAction,
    IReadOnlyList<string> SupportingFacts,
    IReadOnlyList<SocialPlatform> Platforms,
    BriefPreferencesDto Preferences,
    bool ReadyForGeneration,
    IReadOnlyList<ReadinessGap> MissingRequirements,
    byte[] RowVersion);

public sealed record BriefCampaignDto(Guid Id, string Name, CampaignObjective Objective, string Description, string TargetAudience, CampaignStatus Status);

public sealed record BriefBrandContextDto(string BusinessName, string Industry, string BusinessDescription, BrandTone BrandTone, string? Website);

public sealed record BriefCallToActionDto(CallToActionType Type, string? Url);

/// <summary>Tone is resolved: the campaign override, else the brand tone (null only while no brand profile exists).</summary>
public sealed record BriefPreferencesDto(BrandTone? Tone, ContentLength Length, HashtagMode Hashtags, EmojiMode Emojis);
