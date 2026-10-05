using System.Net;
using System.Net.Http.Json;
using SocialMediaAutomation.Application.Campaigns.DTOs;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Infrastructure.Persistence.Seed;
using SocialMediaAutomation.IntegrationTests.Infrastructure;

namespace SocialMediaAutomation.IntegrationTests;

[Collection(ApiCollection.Name)]
public class CampaignTests(ApiFactory factory)
{
    private static readonly string OrgA = $"/api/v1/organizations/{DemoData.OrganizationAId}";
    private static readonly string CampaignsA = $"{OrgA}/campaigns";

    private static object Fact(string text = "Brightline Close is used by 40 customers", string? source = "CRM export, Oct 2026") =>
        new { Text = text, SourceNote = source };

    private async Task<(HttpClient Client, CampaignDto Campaign)> CreateAsAsync(DemoData.DemoUser user, params string[] platforms)
    {
        var client = await factory.SignInAsync(user);
        return (client, await client.CreateCampaignAsync(OrgA, platforms));
    }

    // ---- Basics, details and defaults ----------------------------------------------------------------

    [Fact]
    public async Task Creator_creates_campaign_with_basics_only_and_server_defaults()
    {
        var creator = await factory.SignInAsync(DemoData.CreatorA);

        var response = await creator.PostAsJsonAsync(CampaignsA, new
        {
            Name = "AI Reporting Dashboard Launch", Objective = "PRODUCT_LAUNCH",
            Description = "We are launching a new AI reporting dashboard.", TargetAudience = "Small business owners",
        });
        var created = await response.ReadAsync<CampaignDto>();
        var reloaded = await (await creator.GetAsync($"{CampaignsA}/{created.Id}")).ReadAsync<CampaignDto>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal((DemoData.OrganizationAId, CampaignStatus.Draft, CampaignObjective.PRODUCT_LAUNCH), (reloaded.OrganizationId, reloaded.Status, reloaded.Objective));
        Assert.Equal((CallToActionType.NONE, ContentLength.MEDIUM, HashtagMode.AUTO, EmojiMode.AUTO, (BrandTone?)null),
            (reloaded.CallToAction, reloaded.ContentLength, reloaded.HashtagMode, reloaded.EmojiMode, reloaded.ToneOverride));
        Assert.Empty(reloaded.Platforms);
        Assert.Contains(await (await creator.GetAsync(CampaignsA)).ReadAsync<List<CampaignSummaryDto>>(), c => c.Id == created.Id);
    }

    [Fact]
    public async Task Target_audience_defaults_from_brand_profile_and_is_stored_on_the_campaign()
    {
        var creator = await factory.SignInAsync(DemoData.CreatorA);
        var brandAudience = (await (await creator.GetAsync($"{OrgA}/brand-profile")).ReadAsync<Application.BrandProfiles.DTOs.BrandProfileDto>()).TargetAudience;

        var campaign = await creator.CreateCampaignAsync(OrgA, ApiClient.CampaignBody(targetAudience: null));

        Assert.Equal(brandAudience, campaign.TargetAudience);
    }

    [Fact]
    public async Task Campaign_cannot_be_created_before_the_brand_profile()
    {
        var adminB = await factory.SignInAsync(DemoData.AdminB);
        var workspace = await adminB.CreateWorkspaceAsync(withBrand: false);

        await (await adminB.PostAsJsonAsync($"/api/v1/organizations/{workspace.Id}/campaigns", ApiClient.CampaignBody()))
            .AssertErrorAsync(HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task Details_and_advanced_options_are_updated_together()
    {
        var (creator, campaign) = await CreateAsAsync(DemoData.CreatorA);

        var updated = await (await creator.PutJsonAsync($"{CampaignsA}/{campaign.Id}", new
        {
            campaign.Name, Objective = "WEBSITE_TRAFFIC", campaign.Description, campaign.TargetAudience,
            CallToAction = "VISIT_WEBSITE", CallToActionUrl = "https://brightline.example",
            ToneOverride = "Conversational", ContentLength = "SHORT", HashtagMode = "NONE", EmojiMode = "NONE",
        }, ApiClient.ETag(campaign.RowVersion))).ReadAsync<CampaignDto>();

        Assert.Equal((CampaignObjective.WEBSITE_TRAFFIC, CallToActionType.VISIT_WEBSITE, (BrandTone?)BrandTone.Conversational, ContentLength.SHORT, HashtagMode.NONE, EmojiMode.NONE),
            (updated.Objective, updated.CallToAction, updated.ToneOverride, updated.ContentLength, updated.HashtagMode, updated.EmojiMode));
    }

    [Theory]
    [InlineData("TikTok_growth")]
    [InlineData("Generate demo requests")]
    public async Task Invalid_objective_is_rejected(string objective)
    {
        var creator = await factory.SignInAsync(DemoData.CreatorA);

        await (await creator.PostAsJsonAsync(CampaignsA, ApiClient.CampaignBody(objective: objective)))
            .AssertErrorAsync(HttpStatusCode.BadRequest, ErrorCodes.InvalidCampaignObjective);
    }

    [Theory]
    [InlineData("BOOK_DEMO", "not a url")]
    [InlineData("BOOK_DEMO", "http://brightline.example/demo")] // CTA links must be https
    [InlineData("BOOK_DEMO", "javascript:alert(1)")]
    [InlineData("SIGN_UP", null)] // a link CTA without a link
    public async Task Invalid_or_missing_cta_url_is_rejected(string cta, string? url)
    {
        var creator = await factory.SignInAsync(DemoData.CreatorA);

        await (await creator.PostAsJsonAsync(CampaignsA, ApiClient.CampaignBody(callToAction: cta, callToActionUrl: url)))
            .AssertErrorAsync(HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task Cta_url_is_not_required_when_the_cta_needs_no_link()
    {
        var creator = await factory.SignInAsync(DemoData.CreatorA);

        var campaign = await creator.CreateCampaignAsync(OrgA, ApiClient.CampaignBody(callToAction: "CONTACT_US", callToActionUrl: null));

        Assert.Equal((CallToActionType.CONTACT_US, (string?)null), (campaign.CallToAction, campaign.CallToActionUrl));
    }

    [Fact]
    public async Task Reviewer_can_read_but_not_create_or_change_campaigns()
    {
        var (_, campaign) = await CreateAsAsync(DemoData.CreatorA);
        var reviewer = await factory.SignInAsync(DemoData.ReviewerA);

        await (await reviewer.GetAsync($"{CampaignsA}/{campaign.Id}")).ReadAsync<CampaignDto>();
        await (await reviewer.PostAsJsonAsync(CampaignsA, ApiClient.CampaignBody())).AssertErrorAsync(HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        await (await reviewer.PutJsonAsync($"{CampaignsA}/{campaign.Id}/platforms", new { Platforms = new[] { "LinkedIn" } }, ApiClient.ETag(campaign.RowVersion)))
            .AssertErrorAsync(HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
    }

    [Fact]
    [Trait("Category", "Acceptance")]
    [Trait("Acceptance", "05 stale edit rejected")]
    public async Task Stale_campaign_update_is_rejected()
    {
        var (creator, campaign) = await CreateAsAsync(DemoData.CreatorA);
        var url = $"{CampaignsA}/{campaign.Id}";

        var updated = await (await creator.PutJsonAsync(url, ApiClient.CampaignBody("Renamed"), ApiClient.ETag(campaign.RowVersion))).ReadAsync<CampaignDto>();
        var stale = await creator.PutJsonAsync(url, ApiClient.CampaignBody("Lost update"), ApiClient.ETag(campaign.RowVersion));
        var stalePlatforms = await creator.PutJsonAsync($"{url}/platforms", new { Platforms = new[] { "LinkedIn" } }, ApiClient.ETag(campaign.RowVersion));
        var missing = await creator.PutJsonAsync(url, ApiClient.CampaignBody("No precondition"), ifMatch: null);

        Assert.Equal("Renamed", updated.Name);
        await stale.AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.ConcurrencyConflict);
        await stalePlatforms.AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.ConcurrencyConflict);
        await missing.AssertErrorAsync(HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task Archived_campaign_is_hidden_by_default_and_read_only()
    {
        var (creator, campaign) = await CreateAsAsync(DemoData.CreatorA, "LinkedIn");
        var url = $"{CampaignsA}/{campaign.Id}";

        var archived = await (await creator.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"{url}/archive")
        {
            Headers = { { "If-Match", ApiClient.ETag(campaign.RowVersion) } },
        })).ReadAsync<CampaignDto>();
        var edit = await creator.PutJsonAsync(url, ApiClient.CampaignBody("After archive"), ApiClient.ETag(archived.RowVersion));
        var addFact = await creator.PostAsJsonAsync($"{url}/facts", Fact());
        var active = await (await creator.GetAsync(CampaignsA)).ReadAsync<List<CampaignSummaryDto>>();
        var all = await (await creator.GetAsync($"{CampaignsA}?includeArchived=true")).ReadAsync<List<CampaignSummaryDto>>();
        var brief = await (await creator.GetAsync($"{url}/review")).ReadAsync<CampaignBriefDto>();

        Assert.Equal(CampaignStatus.Archived, archived.Status);
        await edit.AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.InvalidStateTransition);
        await addFact.AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.InvalidStateTransition);
        Assert.DoesNotContain(active, c => c.Id == campaign.Id);
        Assert.Contains(all, c => c.Id == campaign.Id);
        Assert.False(brief.ReadyForGeneration);
    }

    // ---- Platforms --------------------------------------------------------------------------------

    [Fact]
    public async Task Platform_selection_is_stored_normalized_and_can_be_replaced()
    {
        var (creator, campaign) = await CreateAsAsync(DemoData.CreatorA, "INSTAGRAM", "LINKEDIN", "LinkedIn");
        Assert.Equal([SocialPlatform.LinkedIn, SocialPlatform.Instagram], campaign.Platforms);

        var replaced = await (await creator.PutJsonAsync($"{CampaignsA}/{campaign.Id}/platforms",
            new { Platforms = new[] { "Facebook" } }, ApiClient.ETag(campaign.RowVersion))).ReadAsync<CampaignDto>();
        var summary = (await (await creator.GetAsync(CampaignsA)).ReadAsync<List<CampaignSummaryDto>>()).Single(c => c.Id == campaign.Id);

        Assert.Equal([SocialPlatform.Facebook], replaced.Platforms);
        Assert.Equal([SocialPlatform.Facebook], summary.Platforms);
    }

    [Theory]
    [InlineData("TikTok")]
    [InlineData("1")]
    public async Task Unsupported_platform_value_is_rejected(string platform)
    {
        var (creator, campaign) = await CreateAsAsync(DemoData.CreatorA);
        var url = $"{CampaignsA}/{campaign.Id}/platforms";

        await (await creator.PutJsonAsync(url, new { Platforms = new[] { "LinkedIn", platform } }, ApiClient.ETag(campaign.RowVersion)))
            .AssertErrorAsync(HttpStatusCode.BadRequest, ErrorCodes.InvalidPlatform);
        await (await creator.PutJsonAsync(url, new { Platforms = new[] { 1 } }, ApiClient.ETag(campaign.RowVersion)))
            .AssertErrorAsync(HttpStatusCode.BadRequest, ErrorCodes.InvalidPlatform);
    }

    [Fact]
    public async Task Empty_platform_selection_is_rejected()
    {
        var (creator, campaign) = await CreateAsAsync(DemoData.CreatorA);

        await (await creator.PutJsonAsync($"{CampaignsA}/{campaign.Id}/platforms", new { Platforms = Array.Empty<string>() }, ApiClient.ETag(campaign.RowVersion)))
            .AssertErrorAsync(HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
    }

    // ---- Supporting facts -------------------------------------------------------------------------

    [Fact]
    public async Task Supporting_facts_persist_as_stable_fact_rows()
    {
        var (creator, campaign) = await CreateAsAsync(DemoData.CreatorA);
        var url = $"{CampaignsA}/{campaign.Id}/facts";

        var first = await (await creator.PutJsonAsync(url, new { SupportingFacts = new[] { "Launch date is October 20", "Supports PDF and Excel exports", " " } },
            ApiClient.ETag(campaign.RowVersion))).ReadAsync<CampaignDto>();
        var second = await (await creator.PutJsonAsync(url, new { SupportingFacts = new[] { "Supports PDF and Excel exports", "Available to existing customers" } },
            ApiClient.ETag(first.RowVersion))).ReadAsync<CampaignDto>();
        var stale = await creator.PutJsonAsync(url, new { SupportingFacts = new[] { "Stale" } }, ApiClient.ETag(first.RowVersion));
        var reloaded = await (await creator.GetAsync($"{CampaignsA}/{campaign.Id}")).ReadAsync<CampaignDto>();

        Assert.Equal(["Launch date is October 20", "Supports PDF and Excel exports"], first.Facts.Select(f => f.Text));
        Assert.Equal(["Supports PDF and Excel exports", "Available to existing customers"], reloaded.Facts.Select(f => f.Text));
        // An unchanged line keeps its fact id, so earlier AI snapshots stay resolvable.
        Assert.Equal(first.Facts[1].Id, second.Facts.Single(f => f.Text == "Supports PDF and Excel exports").Id);
        Assert.All(reloaded.Facts, f => Assert.Equal(DemoData.CreatorA.Id, f.SuppliedByUserId));
        await stale.AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.ConcurrencyConflict);
    }

    [Fact]
    public async Task Facts_are_added_updated_and_removed_with_provenance_and_supplier()
    {
        var (creator, campaign) = await CreateAsAsync(DemoData.CreatorA);
        var factsUrl = $"{CampaignsA}/{campaign.Id}/facts";

        var added = await (await creator.PostAsJsonAsync(factsUrl, Fact())).ReadAsync<CampaignFactDto>();
        var updated = await (await creator.PutJsonAsync($"{factsUrl}/{added.Id}", Fact("Used by 42 customers", source: null), ApiClient.ETag(added.RowVersion)))
            .ReadAsync<CampaignFactDto>();
        var stale = await creator.PutJsonAsync($"{factsUrl}/{added.Id}", Fact("Stale"), ApiClient.ETag(added.RowVersion));

        Assert.Equal((DemoData.CreatorA.Id, DemoData.CreatorA.DisplayName, "CRM export, Oct 2026"), (added.SuppliedByUserId, added.SuppliedByName, added.SourceNote));
        Assert.Equal((added.Id, "Used by 42 customers", (string?)null), (updated.Id, updated.Text, updated.SourceNote));
        await stale.AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.ConcurrencyConflict);

        var remove = new HttpRequestMessage(HttpMethod.Delete, $"{factsUrl}/{added.Id}") { Headers = { { "If-Match", ApiClient.ETag(updated.RowVersion) } } };
        Assert.Equal(HttpStatusCode.NoContent, (await creator.SendAsync(remove)).StatusCode);
        Assert.Empty((await (await creator.GetAsync($"{CampaignsA}/{campaign.Id}")).ReadAsync<CampaignDto>()).Facts);
    }

    [Fact]
    public async Task Fact_is_only_reachable_through_its_own_campaign()
    {
        var (creator, campaign1) = await CreateAsAsync(DemoData.CreatorA);
        var campaign2 = await creator.CreateCampaignAsync(OrgA);
        var fact = await (await creator.PostAsJsonAsync($"{CampaignsA}/{campaign1.Id}/facts", Fact())).ReadAsync<CampaignFactDto>();

        var viaOtherCampaign = await creator.PutJsonAsync($"{CampaignsA}/{campaign2.Id}/facts/{fact.Id}", Fact("Moved"), ApiClient.ETag(fact.RowVersion));

        await viaOtherCampaign.AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
        Assert.Equal(fact.Text, Assert.Single((await (await creator.GetAsync($"{CampaignsA}/{campaign1.Id}")).ReadAsync<CampaignDto>()).Facts).Text);
    }

    // ---- Review / generation readiness -------------------------------------------------------------

    [Fact]
    public async Task Incomplete_campaign_is_not_ready_and_says_what_is_missing()
    {
        var (creator, campaign) = await CreateAsAsync(DemoData.CreatorA);

        var brief = await (await creator.GetAsync($"{CampaignsA}/{campaign.Id}/review")).ReadAsync<CampaignBriefDto>();

        Assert.False(brief.ReadyForGeneration);
        Assert.Equal(["PLATFORMS"], brief.MissingRequirements.Select(m => m.Code));
        await (await creator.PostAsJsonAsync($"{CampaignsA}/{campaign.Id}/ai-generations", new { }))
            .AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.CampaignNotReadyForGeneration);
    }

    [Fact]
    public async Task Complete_campaign_returns_the_resolved_brief_and_is_ready()
    {
        var (creator, campaign) = await CreateAsAsync(DemoData.CreatorA, "LinkedIn", "Instagram");
        await creator.PutJsonAsync($"{CampaignsA}/{campaign.Id}/facts", new { SupportingFacts = new[] { "Launch date is October 20" } }, ApiClient.ETag(campaign.RowVersion));

        var brief = await (await creator.GetAsync($"{CampaignsA}/{campaign.Id}/review")).ReadAsync<CampaignBriefDto>();

        Assert.True(brief.ReadyForGeneration);
        Assert.Empty(brief.MissingRequirements);
        Assert.Equal((campaign.Name, CampaignObjective.LEAD_GENERATION, "Finance leaders"), (brief.Campaign.Name, brief.Campaign.Objective, brief.Campaign.TargetAudience));
        Assert.Equal(("Demo Organization A", BrandTone.Professional), (brief.BrandContext!.BusinessName, brief.BrandContext.BrandTone));
        Assert.Equal((CallToActionType.BOOK_DEMO, "https://brightline.example/demo"), (brief.CallToAction.Type, brief.CallToAction.Url));
        Assert.Equal(["Launch date is October 20"], brief.SupportingFacts);
        Assert.Equal([SocialPlatform.LinkedIn, SocialPlatform.Instagram], brief.Platforms);
        // No override: the tone resolves to the brand tone; other options carry their defaults.
        Assert.Equal(((BrandTone?)BrandTone.Professional, ContentLength.MEDIUM, HashtagMode.AUTO, EmojiMode.AUTO),
            (brief.Preferences.Tone, brief.Preferences.Length, brief.Preferences.Hashtags, brief.Preferences.Emojis));
    }

    [Fact]
    public async Task Connected_social_accounts_are_not_required_for_generation_readiness()
    {
        var adminB = await factory.SignInAsync(DemoData.AdminB);
        var workspace = await adminB.CreateWorkspaceAsync();
        var orgUrl = $"/api/v1/organizations/{workspace.Id}";

        var campaign = await adminB.CreateCampaignAsync(orgUrl, "LinkedIn");
        var brief = await (await adminB.GetAsync($"{orgUrl}/campaigns/{campaign.Id}/review")).ReadAsync<CampaignBriefDto>();

        Assert.Empty(await (await adminB.GetAsync($"{orgUrl}/social-accounts")).ReadAsync<List<object>>());
        Assert.True(brief.ReadyForGeneration);
    }

    // ---- Tenant isolation --------------------------------------------------------------------------

    [Fact]
    public async Task Campaign_belongs_to_its_workspace_and_is_invisible_to_others()
    {
        var (_, campaign) = await CreateAsAsync(DemoData.CreatorA);
        var adminB = await factory.SignInAsync(DemoData.AdminB);
        var otherUrl = $"/api/v1/organizations/{(await adminB.CreateWorkspaceAsync()).Id}/campaigns";
        var etag = ApiClient.ETag(campaign.RowVersion);

        // Through org A's route (not a member) and through B's own workspace route (member, but wrong owner).
        foreach (var campaigns in new[] { CampaignsA, otherUrl })
        {
            var url = $"{campaigns}/{campaign.Id}";
            await (await adminB.GetAsync(url)).AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
            await (await adminB.GetAsync($"{url}/review")).AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
            await (await adminB.PutJsonAsync(url, ApiClient.CampaignBody("Hijacked"), etag)).AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
            await (await adminB.PutJsonAsync($"{url}/platforms", new { Platforms = new[] { "LinkedIn" } }, etag)).AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
            await (await adminB.PutJsonAsync($"{url}/facts", new { SupportingFacts = new[] { "x" } }, etag)).AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
            await (await adminB.PostAsJsonAsync($"{url}/facts", Fact())).AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
        }
        Assert.DoesNotContain(await (await adminB.GetAsync(otherUrl)).ReadAsync<List<CampaignSummaryDto>>(), c => c.Id == campaign.Id);
    }

    [Fact]
    public async Task Fact_of_another_workspace_cannot_be_changed_through_own_campaign()
    {
        var (creatorA, campaignA) = await CreateAsAsync(DemoData.CreatorA);
        var factA = await (await creatorA.PostAsJsonAsync($"{CampaignsA}/{campaignA.Id}/facts", Fact())).ReadAsync<CampaignFactDto>();
        var adminB = await factory.SignInAsync(DemoData.AdminB);
        var orgB = $"/api/v1/organizations/{(await adminB.CreateWorkspaceAsync()).Id}";
        var campaignB = await adminB.CreateCampaignAsync(orgB);

        var update = await adminB.PutJsonAsync($"{orgB}/campaigns/{campaignB.Id}/facts/{factA.Id}", Fact("Hijacked"), ApiClient.ETag(factA.RowVersion));
        var remove = await adminB.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"{orgB}/campaigns/{campaignB.Id}/facts/{factA.Id}")
        {
            Headers = { { "If-Match", ApiClient.ETag(factA.RowVersion) } },
        });

        await update.AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
        await remove.AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
        Assert.Equal(factA.Text, Assert.Single((await (await creatorA.GetAsync($"{CampaignsA}/{campaignA.Id}")).ReadAsync<CampaignDto>()).Facts).Text);
    }
}
