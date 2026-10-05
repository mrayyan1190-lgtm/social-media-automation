using SocialMediaAutomation.Domain.Common;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Domain.Entities;

/// <summary>
/// The business intent behind a set of platform posts (PRD §11). One aggregate: the wizard steps that fill it in are
/// a frontend concept. Platforms are chosen separately and may be empty until the campaign is ready for generation.
/// </summary>
public sealed class Campaign
{
    public const int ShortTextMax = 200;
    public const int LongTextMax = 2000;

    private readonly List<CampaignPlatform> platformSelections = [];

    private Campaign() { }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = null!;
    public CampaignObjective Objective { get; private set; }

    /// <summary>What the campaign promotes or communicates: topic, main message and product/service context in one field.</summary>
    public string Description { get; private set; } = null!;

    /// <summary>Always stored; defaults to the brand profile's audience when the user leaves it empty.</summary>
    public string TargetAudience { get; private set; } = null!;

    public CallToActionType CallToAction { get; private set; }
    public string? CallToActionUrl { get; private set; }

    /// <summary>Null means "use the brand tone".</summary>
    public BrandTone? ToneOverride { get; private set; }

    public ContentLength ContentLength { get; private set; }
    public HashtagMode HashtagMode { get; private set; }
    public EmojiMode EmojiMode { get; private set; }
    public CampaignStatus Status { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyCollection<CampaignPlatform> PlatformSelections => platformSelections;

    public IReadOnlyList<SocialPlatform> Platforms => platformSelections.Select(p => p.Platform).Order().ToList();

    public bool IsArchived => Status == CampaignStatus.Archived;

    /// <param name="brandTargetAudience">Used when the campaign leaves its own target audience empty.</param>
    public static Campaign Create(Guid id, Guid organizationId, CampaignDetails details, string? brandTargetAudience, Guid userId, DateTime utcNow)
    {
        var campaign = new Campaign
        {
            Id = id, OrganizationId = organizationId, Status = CampaignStatus.Draft, CreatedByUserId = userId, CreatedAtUtc = utcNow,
        };
        campaign.Apply(details, brandTargetAudience, userId, utcNow);
        return campaign;
    }

    public void Update(CampaignDetails details, string? brandTargetAudience, Guid userId, DateTime utcNow)
    {
        EnsureEditable();
        Apply(details, brandTargetAudience, userId, utcNow);
    }

    /// <summary>Replaces the platform selection, keeping rows for platforms that stay selected.</summary>
    public void SetPlatforms(IReadOnlyCollection<SocialPlatform>? platforms, Guid userId, DateTime utcNow)
    {
        EnsureEditable();
        var wanted = (platforms ?? []).Select(p => Guard.Defined(p, "Platforms")).Distinct().ToList();
        if (wanted.Count == 0)
            throw Guard.Invalid("Platforms", "must include at least one of LinkedIn, Facebook, Instagram");

        platformSelections.RemoveAll(s => !wanted.Contains(s.Platform));
        foreach (var platform in wanted.Where(p => platformSelections.All(s => s.Platform != p)))
            platformSelections.Add(new CampaignPlatform(OrganizationId, Id, platform));
        Touch(userId, utcNow);
    }

    /// <summary>Records a change to a part of the aggregate stored in other rows (facts), so the row version moves too.</summary>
    public void Touch(Guid userId, DateTime utcNow)
    {
        UpdatedByUserId = userId;
        UpdatedAtUtc = utcNow;
    }

    /// <summary>The first generation request moves a draft campaign to ACTIVE.</summary>
    public void MarkActive(Guid userId, DateTime utcNow)
    {
        EnsureEditable();
        if (Status == CampaignStatus.Draft)
        {
            Status = CampaignStatus.Active;
            Touch(userId, utcNow);
        }
    }

    public void Archive(Guid userId, DateTime utcNow)
    {
        EnsureEditable();
        Status = CampaignStatus.Archived;
        Touch(userId, utcNow);
    }

    /// <summary>Archived campaigns are read-only, including their facts and platforms.</summary>
    public void EnsureEditable()
    {
        if (IsArchived)
            throw new BusinessException(ErrorCodes.InvalidStateTransition, "Archived campaigns cannot be changed.");
    }

    /// <summary>
    /// What still blocks AI generation (PRD §11.1). Supporting facts and connected social accounts are not required.
    /// Saved fields are already validated; these checks also catch rows written before the current rules.
    /// </summary>
    public IReadOnlyList<ReadinessGap> GenerationGaps(bool brandProfileExists)
    {
        var gaps = new List<ReadinessGap>();
        if (IsArchived)
            gaps.Add(new("CAMPAIGN_ARCHIVED", "Archived campaigns cannot generate content."));
        if (!brandProfileExists)
            gaps.Add(new("BRAND_PROFILE", "Complete the brand profile."));
        if (string.IsNullOrWhiteSpace(Name))
            gaps.Add(new("NAME", "Enter a campaign name."));
        if (!Enum.IsDefined(Objective))
            gaps.Add(new("OBJECTIVE", "Choose a campaign objective."));
        if (string.IsNullOrWhiteSpace(Description))
            gaps.Add(new("DESCRIPTION", "Describe what the campaign promotes or communicates."));
        if (string.IsNullOrWhiteSpace(TargetAudience))
            gaps.Add(new("TARGET_AUDIENCE", "Enter a target audience."));
        if (platformSelections.Count == 0)
            gaps.Add(new("PLATFORMS", "Select at least one platform."));
        if (CallToActions.RequiresUrl(CallToAction) && !IsHttpsUrl(CallToActionUrl))
            gaps.Add(new("CALL_TO_ACTION_URL", $"{CallToAction} needs a valid https URL."));
        return gaps;
    }

    private static bool IsHttpsUrl(string? url)
    {
        try
        {
            return Guard.OptionalHttpUrl(url, "URL", httpsOnly: true) is not null;
        }
        catch (BusinessException)
        {
            return false;
        }
    }

    private void Apply(CampaignDetails details, string? brandTargetAudience, Guid userId, DateTime utcNow)
    {
        Name = Guard.Required(details.Name, "Campaign name", ShortTextMax);
        Objective = Guard.Defined(details.Objective, "Objective");
        Description = Guard.Required(details.Description, "Description", LongTextMax);
        TargetAudience = Guard.Optional(details.TargetAudience, "Target audience", LongTextMax)
            ?? Guard.Optional(brandTargetAudience, "Target audience", LongTextMax)
            ?? throw Guard.Invalid("Target audience", "is required because the brand profile has no target audience");
        CallToAction = Guard.Defined(details.CallToAction, "Call to action");
        CallToActionUrl = Guard.OptionalHttpUrl(details.CallToActionUrl, "Call to action URL", httpsOnly: true);
        if (CallToActions.RequiresUrl(CallToAction) && CallToActionUrl is null)
            throw Guard.Invalid("Call to action URL", $"is required for {CallToAction}");
        ToneOverride = details.ToneOverride is { } tone ? Guard.Defined(tone, "Tone override") : null;
        ContentLength = Guard.Defined(details.ContentLength, "Content length");
        HashtagMode = Guard.Defined(details.HashtagMode, "Hashtag mode");
        EmojiMode = Guard.Defined(details.EmojiMode, "Emoji mode");
        Touch(userId, utcNow);
    }
}

/// <summary>Campaign input. Optional choices default server-side so the user never has to configure them.</summary>
public sealed record CampaignDetails(
    string Name,
    CampaignObjective Objective,
    string Description,
    string? TargetAudience,
    CallToActionType CallToAction = CallToActionType.NONE,
    string? CallToActionUrl = null,
    BrandTone? ToneOverride = null,
    ContentLength ContentLength = ContentLength.MEDIUM,
    HashtagMode HashtagMode = HashtagMode.AUTO,
    EmojiMode EmojiMode = EmojiMode.AUTO);

/// <summary>One selected platform of a campaign (normalized; one row per platform).</summary>
public sealed class CampaignPlatform
{
    private CampaignPlatform() { }

    internal CampaignPlatform(Guid organizationId, Guid campaignId, SocialPlatform platform)
    {
        OrganizationId = organizationId;
        CampaignId = campaignId;
        Platform = platform;
    }

    public Guid OrganizationId { get; private set; }
    public Guid CampaignId { get; private set; }
    public SocialPlatform Platform { get; private set; }
}

/// <summary>A stable code the frontend can act on, with a human message.</summary>
public sealed record ReadinessGap(string Code, string Message);
