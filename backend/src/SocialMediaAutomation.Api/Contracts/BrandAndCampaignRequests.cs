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

/// <summary>A campaign folder: name and colour (defaults to blue when omitted), optional short description.</summary>
public sealed record SaveCampaignRequest(string Name, string? Color, string? Description)
{
    public CampaignDetails ToDetails() => new(Name, Color, Description);
}
