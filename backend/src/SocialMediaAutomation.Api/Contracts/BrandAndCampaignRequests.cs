using System.ComponentModel.DataAnnotations;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Api.Contracts;

// Transport shape only. Authoritative validation (required fields, lengths, URLs, platforms) lives in the domain.

public sealed record SaveBrandProfileRequest(
    string BusinessDescription,
    string TargetAudience,
    [Required] BrandTone? BrandTone,
    string? Website,
    string[]? ProhibitedTerms)
{
    public BrandProfileDetails ToDetails() => new(BusinessDescription, TargetAudience, BrandTone!.Value, Website, ProhibitedTerms);
}

/// <summary>
/// Campaign basics, details and advanced options in one body. Only name and description (the campaign prompt) are required; the objective defaults to AUTO,
/// omitted options get server-side defaults (CTA NONE, length MEDIUM, hashtags/emojis AUTO, brand tone, brand audience).
/// </summary>
public sealed record SaveCampaignRequest(
    string Name,
    CampaignObjective? Objective,
    string Description,
    string? TargetAudience,
    CallToActionType? CallToAction,
    string? CallToActionUrl,
    BrandTone? ToneOverride,
    ContentLength? ContentLength,
    HashtagMode? HashtagMode,
    EmojiMode? EmojiMode)
{
    public CampaignDetails ToDetails() => new(
        Name, Objective ?? CampaignObjective.AUTO, Description, TargetAudience, CallToAction ?? CallToActionType.NONE, CallToActionUrl, ToneOverride,
        ContentLength ?? Domain.Enums.ContentLength.MEDIUM, HashtagMode ?? Domain.Enums.HashtagMode.AUTO, EmojiMode ?? Domain.Enums.EmojiMode.AUTO);
}

public sealed record SetCampaignPlatformsRequest(SocialPlatform[]? Platforms);

public sealed record ReplaceCampaignFactsRequest(string[]? SupportingFacts);

public sealed record SaveCampaignFactRequest(string Text, string? SourceNote);
