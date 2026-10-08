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

