using SocialMediaAutomation.Domain.Common;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Domain.Entities;

/// <summary>
/// Reusable brand context for AI generation. Exactly one per Organization in the MVP (PRD §10). The business name,
/// industry and language come from the Organization.
/// </summary>
public sealed class BrandProfile
{
    public const int LongTextMax = 2000;

    private BrandProfile() { }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string BusinessDescription { get; private set; } = null!;

    /// <summary>Default audience for generated content.</summary>
    public string TargetAudience { get; private set; } = null!;

    public BrandTone BrandTone { get; private set; }

    /// <summary>Metadata only. The MVP never fetches or scrapes it, and it is not a factual source (PRD §10).</summary>
    public string? Website { get; private set; }

    /// <summary>Optional. Terms the brand never uses; enforced deterministically on every version before review.</summary>
    public List<string> ProhibitedTerms { get; private set; } = [];

    public Guid UpdatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static BrandProfile Create(Guid id, Guid organizationId, BrandProfileDetails details, Guid userId, DateTime utcNow)
    {
        var profile = new BrandProfile { Id = id, OrganizationId = organizationId, CreatedAtUtc = utcNow };
        profile.Update(details, userId, utcNow);
        return profile;
    }

    public void Update(BrandProfileDetails details, Guid userId, DateTime utcNow)
    {
        BusinessDescription = Guard.Required(details.BusinessDescription, "Business description", LongTextMax);
        TargetAudience = Guard.Required(details.TargetAudience, "Target audience", LongTextMax);
        BrandTone = Guard.Defined(details.BrandTone, "Brand tone");
        Website = Guard.OptionalHttpUrl(details.Website, "Website");
        ProhibitedTerms = Guard.TextList(details.ProhibitedTerms, "Prohibited terms", maxItems: 100, maxItemLength: 100);
        UpdatedByUserId = userId;
        UpdatedAtUtc = utcNow;
    }
}

public sealed record BrandProfileDetails(
    string BusinessDescription,
    string TargetAudience,
    BrandTone BrandTone,
    string? Website,
    IReadOnlyList<string>? ProhibitedTerms = null);
