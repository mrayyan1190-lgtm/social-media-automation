using System.Net;
using System.Net.Http.Json;
using SocialMediaAutomation.Application.Campaigns.DTOs;
using SocialMediaAutomation.Application.Posts.DTOs;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Infrastructure.Persistence.Seed;
using SocialMediaAutomation.IntegrationTests.Infrastructure;

namespace SocialMediaAutomation.IntegrationTests;

/// <summary>A campaign is a folder for related posts: name, colour, optional description.</summary>
[Collection(ApiCollection.Name)]
public class CampaignTests(ApiFactory factory)
{
    private static readonly string OrgA = $"/api/v1/organizations/{DemoData.OrganizationAId}";
    private static readonly string CampaignsA = $"{OrgA}/campaigns";

    private async Task<(HttpClient Client, CampaignDto Campaign)> CreateAsAsync(DemoData.DemoUser user)
    {
        var client = await factory.SignInAsync(user);
        return (client, await client.CreateCampaignAsync(OrgA));
    }

    private static Task<HttpResponseMessage> ArchiveAsync(HttpClient client, CampaignDto campaign) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"{CampaignsA}/{campaign.Id}/archive")
        {
            Headers = { { "If-Match", ApiClient.ETag(campaign.RowVersion) } },
        });

    [Fact]
    public async Task Creator_creates_a_campaign_with_name_and_color()
    {
        var creator = await factory.SignInAsync(DemoData.CreatorA);

        var response = await creator.PostAsJsonAsync(CampaignsA, new { Name = "Product Launch", Color = "#16a34a", Description = "Autumn launch posts" });
        var created = await response.ReadAsync<CampaignDto>();
        var reloaded = await (await creator.GetAsync($"{CampaignsA}/{created.Id}")).ReadAsync<CampaignDto>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal((DemoData.OrganizationAId, "Product Launch", "#16A34A", "Autumn launch posts", CampaignStatus.Active),
            (reloaded.OrganizationId, reloaded.Name, reloaded.Color, reloaded.Description, reloaded.Status));
        Assert.Contains(await (await creator.GetAsync(CampaignsA)).ReadAsync<List<CampaignSummaryDto>>(), c => c.Id == created.Id && c.PostCount == 0);
    }

    [Theory]
    [InlineData("", "#2563EB")]
    [InlineData("Launch", "blue")]
    public async Task Missing_name_or_invalid_color_is_rejected(string name, string color)
    {
        var creator = await factory.SignInAsync(DemoData.CreatorA);

        await (await creator.PostAsJsonAsync(CampaignsA, new { Name = name, Color = color }))
            .AssertErrorAsync(HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
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
    public async Task Reviewer_can_read_but_not_create_or_change_campaigns()
    {
        var (_, campaign) = await CreateAsAsync(DemoData.CreatorA);
        var reviewer = await factory.SignInAsync(DemoData.ReviewerA);

        await (await reviewer.GetAsync($"{CampaignsA}/{campaign.Id}")).ReadAsync<CampaignDto>();
        await (await reviewer.PostAsJsonAsync(CampaignsA, ApiClient.CampaignBody())).AssertErrorAsync(HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        await (await reviewer.PutJsonAsync($"{CampaignsA}/{campaign.Id}", ApiClient.CampaignBody("Renamed"), ApiClient.ETag(campaign.RowVersion)))
            .AssertErrorAsync(HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
    }

    [Fact]
    [Trait("Category", "Acceptance")]
    [Trait("Acceptance", "05 stale edit rejected")]
    public async Task Stale_campaign_update_is_rejected()
    {
        var (creator, campaign) = await CreateAsAsync(DemoData.CreatorA);
        var url = $"{CampaignsA}/{campaign.Id}";

        var updated = await (await creator.PutJsonAsync(url, ApiClient.CampaignBody("Renamed", "#DC2626"), ApiClient.ETag(campaign.RowVersion))).ReadAsync<CampaignDto>();
        var stale = await creator.PutJsonAsync(url, ApiClient.CampaignBody("Lost update"), ApiClient.ETag(campaign.RowVersion));
        var missing = await creator.PutJsonAsync(url, ApiClient.CampaignBody("No precondition"), ifMatch: null);

        Assert.Equal(("Renamed", "#DC2626"), (updated.Name, updated.Color));
        await stale.AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.ConcurrencyConflict);
        await missing.AssertErrorAsync(HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task Archived_campaign_is_hidden_by_default_read_only_and_takes_no_new_posts()
    {
        var (creator, campaign) = await CreateAsAsync(DemoData.CreatorA);

        var archived = await (await ArchiveAsync(creator, campaign)).ReadAsync<CampaignDto>();
        var edit = await creator.PutJsonAsync($"{CampaignsA}/{campaign.Id}", ApiClient.CampaignBody("After archive"), ApiClient.ETag(archived.RowVersion));
        var newPost = await creator.CreatePostAsync($"{CampaignsA}/{campaign.Id}", new { Platform = "LinkedIn", Body = "Hello" });
        var active = await (await creator.GetAsync(CampaignsA)).ReadAsync<List<CampaignSummaryDto>>();
        var all = await (await creator.GetAsync($"{CampaignsA}?includeArchived=true")).ReadAsync<List<CampaignSummaryDto>>();

        Assert.Equal(CampaignStatus.Archived, archived.Status);
        await edit.AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.InvalidStateTransition);
        await newPost.AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.InvalidStateTransition);
        Assert.DoesNotContain(active, c => c.Id == campaign.Id);
        Assert.Contains(all, c => c.Id == campaign.Id);
    }

    [Fact]
    public async Task Campaign_lists_its_posts_and_counts_them()
    {
        var (creator, campaign) = await CreateAsAsync(DemoData.CreatorA);
        var url = $"{CampaignsA}/{campaign.Id}";
        await (await creator.CreatePostAsync(url, new { Platform = "LinkedIn", Body = "One" })).ReadAsync<PostDto>();
        await (await creator.CreatePostAsync(url, new { Platform = "Facebook", Body = "Two" })).ReadAsync<PostDto>();

        var posts = await (await creator.GetAsync(ApiClient.PostsOf(url))).ReadAsync<List<PostListItemDto>>();
        var summary = (await (await creator.GetAsync(CampaignsA)).ReadAsync<List<CampaignSummaryDto>>()).Single(c => c.Id == campaign.Id);

        Assert.Equal(2, posts.Count);
        Assert.All(posts, p => Assert.Equal((campaign.Id, campaign.Name, campaign.Color), (p.CampaignId!.Value, p.CampaignName, p.CampaignColor)));
        Assert.Equal(2, summary.PostCount);
    }

    [Fact]
    public async Task Campaign_belongs_to_its_workspace_and_is_invisible_to_others()
    {
        var (_, campaign) = await CreateAsAsync(DemoData.CreatorA);
        var adminB = await factory.SignInAsync(DemoData.AdminB);
        var orgB = $"/api/v1/organizations/{(await adminB.CreateWorkspaceAsync()).Id}";
        var etag = ApiClient.ETag(campaign.RowVersion);

        // Through org A's route (not a member) and through B's own workspace route (member, but wrong owner).
        foreach (var campaigns in new[] { CampaignsA, $"{orgB}/campaigns" })
        {
            var url = $"{campaigns}/{campaign.Id}";
            await (await adminB.GetAsync(url)).AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
            await (await adminB.PutJsonAsync(url, ApiClient.CampaignBody("Hijacked"), etag)).AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
        }
        await (await adminB.GetAsync($"{orgB}/posts?campaignId={campaign.Id}")).AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
        await (await adminB.CreatePostAsync($"{orgB}/campaigns/{campaign.Id}", new { Platform = "LinkedIn", Body = "Hello" }))
            .AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
        Assert.DoesNotContain(await (await adminB.GetAsync($"{orgB}/campaigns")).ReadAsync<List<CampaignSummaryDto>>(), c => c.Id == campaign.Id);
    }
}
