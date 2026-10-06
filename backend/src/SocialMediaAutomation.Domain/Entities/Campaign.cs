using System.Text.RegularExpressions;
using SocialMediaAutomation.Domain.Common;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Domain.Entities;

/// <summary>
/// A folder that groups related posts (name, colour, optional description). It carries no AI brief: content is created
/// in AI Studio or Composer and may be filed into a campaign, or into none.
/// </summary>
public sealed partial class Campaign
{
    public const int ShortTextMax = 200;
    public const int LongTextMax = 2000;
    public const string DefaultColor = "#2563EB";

    private Campaign() { }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = null!;

    /// <summary>#RRGGBB, upper case.</summary>
    public string Color { get; private set; } = DefaultColor;

    public string? Description { get; private set; }
    public CampaignStatus Status { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public bool IsArchived => Status == CampaignStatus.Archived;

    public static Campaign Create(Guid id, Guid organizationId, CampaignDetails details, Guid userId, DateTime utcNow)
    {
        var campaign = new Campaign
        {
            Id = id, OrganizationId = organizationId, Status = CampaignStatus.Active, CreatedByUserId = userId, CreatedAtUtc = utcNow,
        };
        campaign.Apply(details, userId, utcNow);
        return campaign;
    }

    public void Update(CampaignDetails details, Guid userId, DateTime utcNow)
    {
        EnsureEditable();
        Apply(details, userId, utcNow);
    }

    public void Archive(Guid userId, DateTime utcNow)
    {
        EnsureEditable();
        Status = CampaignStatus.Archived;
        UpdatedByUserId = userId;
        UpdatedAtUtc = utcNow;
    }

    /// <summary>Archived campaigns are read-only and accept no new posts.</summary>
    public void EnsureEditable()
    {
        if (IsArchived)
            throw new BusinessException(ErrorCodes.InvalidStateTransition, "Archived campaigns cannot be changed.");
    }

    private void Apply(CampaignDetails details, Guid userId, DateTime utcNow)
    {
        Name = Guard.Required(details.Name, "Campaign name", ShortTextMax);
        var color = details.Color?.Trim() ?? DefaultColor;
        if (!ColorPattern().IsMatch(color))
            throw Guard.Invalid("Campaign color", "must be a hex colour such as #2563EB");
        Color = color.ToUpperInvariant();
        Description = Guard.Optional(details.Description, "Description", LongTextMax);
        UpdatedByUserId = userId;
        UpdatedAtUtc = utcNow;
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex ColorPattern();
}

public sealed record CampaignDetails(string Name, string? Color, string? Description);
