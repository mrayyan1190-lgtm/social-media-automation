using SocialMediaAutomation.Domain.Common;

namespace SocialMediaAutomation.Domain.Entities;

/// <summary>
/// A human-supplied piece of business evidence with provenance (PRD §11.3). Desired outcomes and campaign goals
/// belong in the campaign brief, not here. Removal is a soft delete so the stable Id stays resolvable for
/// later AI input snapshots and fact references.
/// </summary>
public sealed class CampaignFact
{
    public const int TextMax = 1000;
    public const int SourceNoteMax = 500;

    private CampaignFact() { }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid CampaignId { get; private set; }
    public string Text { get; private set; } = null!;
    /// <summary>Optional provenance ("CRM export, Oct 2026"). Facts entered as one line per fact have none.</summary>
    public string? SourceNote { get; private set; }
    public Guid SuppliedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public DateTime? RemovedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public bool IsRemoved => RemovedAtUtc is not null;

    public static CampaignFact Create(Guid id, Campaign campaign, string text, string? sourceNote, Guid userId, DateTime utcNow)
    {
        campaign.EnsureEditable();
        var fact = new CampaignFact { Id = id, OrganizationId = campaign.OrganizationId, CampaignId = campaign.Id, CreatedAtUtc = utcNow };
        fact.Apply(text, sourceNote, userId, utcNow);
        return fact;
    }

    public void Update(Campaign campaign, string text, string? sourceNote, Guid userId, DateTime utcNow)
    {
        campaign.EnsureEditable();
        Apply(text, sourceNote, userId, utcNow);
    }

    public void Remove(Campaign campaign, DateTime utcNow)
    {
        campaign.EnsureEditable();
        RemovedAtUtc = utcNow;
        UpdatedAtUtc = utcNow;
    }

    private void Apply(string text, string? sourceNote, Guid userId, DateTime utcNow)
    {
        Text = Guard.Required(text, "Fact text", TextMax);
        SourceNote = Guard.Optional(sourceNote, "Source/provenance note", SourceNoteMax);
        SuppliedByUserId = userId;
        UpdatedAtUtc = utcNow;
    }
}
