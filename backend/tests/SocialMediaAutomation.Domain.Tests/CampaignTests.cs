using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Domain.Tests;

public class CampaignTests
{
    internal static CampaignDetails Details(
        CallToActionType cta = CallToActionType.BOOK_DEMO,
        string? ctaUrl = "https://example.com/demo",
        string? targetAudience = "Finance leaders") =>
        new("Spring launch", CampaignObjective.PRODUCT_LAUNCH, "We are launching Brightline Close.", targetAudience, cta, ctaUrl);

    /// <summary>A campaign with the given platforms selected, as other domain tests need.</summary>
    internal static Campaign New(params SocialPlatform[] platforms)
    {
        var campaign = Create(Details(), "Brand audience");
        campaign.SetPlatforms(platforms, Guid.NewGuid(), DateTime.UtcNow);
        return campaign;
    }

    private static Campaign Create(CampaignDetails details, string? brandAudience = "Brand audience") =>
        Campaign.Create(Guid.NewGuid(), Guid.NewGuid(), details, brandAudience, Guid.NewGuid(), DateTime.UtcNow);

    [Fact]
    public void New_campaign_is_a_draft_with_server_side_defaults()
    {
        var campaign = Create(new CampaignDetails("Launch", CampaignObjective.ANNOUNCEMENT, "News", null));

        Assert.Equal(CampaignStatus.Draft, campaign.Status);
        Assert.Equal((CallToActionType.NONE, ContentLength.MEDIUM, HashtagMode.AUTO, EmojiMode.AUTO, (BrandTone?)null),
            (campaign.CallToAction, campaign.ContentLength, campaign.HashtagMode, campaign.EmojiMode, campaign.ToneOverride));
        Assert.Empty(campaign.Platforms);
    }

    [Fact]
    public void Platforms_are_deduplicated_ordered_and_replaced()
    {
        var campaign = New(SocialPlatform.Instagram, SocialPlatform.LinkedIn, SocialPlatform.LinkedIn);
        Assert.Equal([SocialPlatform.LinkedIn, SocialPlatform.Instagram], campaign.Platforms);

        campaign.SetPlatforms([SocialPlatform.Facebook, SocialPlatform.LinkedIn], Guid.NewGuid(), DateTime.UtcNow);
        Assert.Equal([SocialPlatform.LinkedIn, SocialPlatform.Facebook], campaign.Platforms);
    }

    [Fact]
    public void Empty_or_undefined_platform_selection_is_rejected()
    {
        var campaign = Create(Details());
        AssertInvalid(() => campaign.SetPlatforms([], Guid.NewGuid(), DateTime.UtcNow));
        AssertInvalid(() => campaign.SetPlatforms([(SocialPlatform)42], Guid.NewGuid(), DateTime.UtcNow));
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("http://example.com/demo")] // CTA links must be https
    [InlineData("ftp://example.com/file")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:secret@example.com/")]
    public void Invalid_cta_url_is_rejected(string url) => AssertInvalid(() => Create(Details(ctaUrl: url)));

    [Fact]
    public void Cta_url_is_required_only_for_link_ctas()
    {
        AssertInvalid(() => Create(Details(CallToActionType.BOOK_DEMO, ctaUrl: null)));
        Assert.Null(Create(Details(CallToActionType.CONTACT_US, ctaUrl: null)).CallToActionUrl);
        Assert.Null(Create(Details(CallToActionType.NONE, ctaUrl: null)).CallToActionUrl);
    }

    [Fact]
    public void Undefined_objective_is_rejected() =>
        AssertInvalid(() => Create(new CampaignDetails("Launch", (CampaignObjective)99, "News", "Audience")));

    [Fact]
    public void Target_audience_defaults_from_brand_and_is_stored()
    {
        Assert.Equal("Brand audience", Create(Details(targetAudience: "  "), brandAudience: "Brand audience").TargetAudience);
        Assert.Equal("Own audience", Create(Details(targetAudience: "Own audience"), brandAudience: "Brand audience").TargetAudience);
        AssertInvalid(() => Create(Details(targetAudience: null), brandAudience: null));
    }

    [Fact]
    public void Readiness_requires_brand_and_platforms_but_not_facts()
    {
        var campaign = Create(Details());
        Assert.Equal(["BRAND_PROFILE", "PLATFORMS"], campaign.GenerationGaps(brandProfileExists: false).Select(g => g.Code));

        campaign.SetPlatforms([SocialPlatform.LinkedIn], Guid.NewGuid(), DateTime.UtcNow);
        Assert.Empty(campaign.GenerationGaps(brandProfileExists: true));
    }

    [Fact]
    public void First_generation_request_activates_a_draft()
    {
        var campaign = New(SocialPlatform.LinkedIn);
        campaign.MarkActive(Guid.NewGuid(), DateTime.UtcNow);
        Assert.Equal(CampaignStatus.Active, campaign.Status);
    }

    [Fact]
    public void Archived_campaign_and_its_facts_are_read_only()
    {
        var campaign = New(SocialPlatform.LinkedIn);
        var fact = CampaignFact.Create(Guid.NewGuid(), campaign, "Used by 40 customers", "CRM export, March", Guid.NewGuid(), DateTime.UtcNow);
        campaign.Archive(Guid.NewGuid(), DateTime.UtcNow);

        Assert.Equal(ErrorCodes.InvalidStateTransition, Assert.Throws<BusinessException>(() => campaign.Update(Details(), null, Guid.NewGuid(), DateTime.UtcNow)).Code);
        Assert.Equal(ErrorCodes.InvalidStateTransition, Assert.Throws<BusinessException>(() => campaign.SetPlatforms([SocialPlatform.Facebook], Guid.NewGuid(), DateTime.UtcNow)).Code);
        Assert.Equal(ErrorCodes.InvalidStateTransition, Assert.Throws<BusinessException>(() => fact.Update(campaign, "x", "y", Guid.NewGuid(), DateTime.UtcNow)).Code);
        Assert.Equal(ErrorCodes.InvalidStateTransition, Assert.Throws<BusinessException>(() =>
            CampaignFact.Create(Guid.NewGuid(), campaign, "x", "y", Guid.NewGuid(), DateTime.UtcNow)).Code);
        Assert.Contains("CAMPAIGN_ARCHIVED", campaign.GenerationGaps(true).Select(g => g.Code));
    }

    [Fact]
    public void Fact_source_note_is_optional_and_fact_inherits_campaign_tenant()
    {
        var campaign = New(SocialPlatform.LinkedIn);

        AssertInvalid(() => CampaignFact.Create(Guid.NewGuid(), campaign, " ", null, Guid.NewGuid(), DateTime.UtcNow));
        var fact = CampaignFact.Create(Guid.NewGuid(), campaign, "Used by 40 customers", " ", Guid.NewGuid(), DateTime.UtcNow);
        Assert.Null(fact.SourceNote);
        Assert.Equal((campaign.OrganizationId, campaign.Id), (fact.OrganizationId, fact.CampaignId));
    }

    private static void AssertInvalid(Action action) =>
        Assert.Equal(ErrorCodes.ValidationFailed, Assert.Throws<BusinessException>(action).Code);
}
