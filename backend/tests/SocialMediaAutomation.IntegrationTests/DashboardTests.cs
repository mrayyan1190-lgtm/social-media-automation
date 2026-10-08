using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SocialMediaAutomation.Application.Dashboard;
using SocialMediaAutomation.Application.Posts.DTOs;
using SocialMediaAutomation.Application.Publications.DTOs;
using SocialMediaAutomation.Application.Reviews.DTOs;
using SocialMediaAutomation.Application.SocialAccounts.DTOs;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Infrastructure.Persistence.Seed;
using SocialMediaAutomation.IntegrationTests.Infrastructure;

namespace SocialMediaAutomation.IntegrationTests;

/// <summary>Dashboard summary and calendar filters through the real API.</summary>
[Collection(ApiCollection.Name)]
public class DashboardTests(ApiFactory factory)
{
    private static readonly string OrgA = $"/api/v1/organizations/{DemoData.OrganizationAId}";
    private static readonly string OrgB = $"/api/v1/organizations/{DemoData.OrganizationBId}";

    private static Task<HttpResponseMessage> PostWithETagAsync(HttpClient client, string url, object? body, byte[] rowVersion) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body ?? new { }, options: ApiClient.Json),
            Headers = { { "If-Match", ApiClient.ETag(rowVersion) } },
        });

    /// <summary>An approved simulated post in workspace A, published now and run through the worker.</summary>
    private async Task<PublicationJobDto> SimulatedPublicationAsync(string platform = "LinkedIn")
    {
        var creator = await factory.SignInAsync(DemoData.CreatorA);
        var account = (await (await creator.GetAsync($"{OrgA}/social-accounts?platform={platform}")).ReadAsync<List<SocialAccountDto>>())
            .First(a => a.ExecutionMode == ExecutionMode.SIMULATED);
        var draft = await (await creator.CreatePostAsync(OrgA,
            new { Platform = platform, Hook = "Hook", Body = "Close the books in days.", SocialAccountId = account.Id })).ReadAsync<PostDto>();
        var review = await (await PostWithETagAsync(creator, $"{OrgA}/posts/{draft.Id}/submit-review", null, draft.RowVersion)).ReadAsync<ReviewSummaryDto>();
        await PostWithETagAsync(await factory.SignInAsync(DemoData.ReviewerA), $"{OrgA}/review-requests/{review.Id}/approve", null, review.RowVersion);
        var post = await (await creator.GetAsync($"{OrgA}/posts/{draft.Id}")).ReadAsync<PostDto>();
        var job = await (await PostWithETagAsync(creator, $"{OrgA}/posts/{post.Id}/publish-now", new { PostVersionId = post.CurrentVersionId }, post.RowVersion))
            .ReadAsync<PublicationJobDto>();
        await factory.DrainPublicationsAsync();
        return job;
    }

    [Fact]
    [Trait("Category", "Acceptance")]
    [Trait("Acceptance", "15 SIMULATED never PUBLISHED")]
    public async Task Dashboard_counts_editorial_and_delivery_states_separately_and_never_counts_simulation_as_published()
    {
        var reviewer = await factory.SignInAsync(DemoData.ReviewerA);
        var before = await (await reviewer.GetAsync($"{OrgA}/dashboard")).ReadAsync<DashboardDto>();

        await SimulatedPublicationAsync();
        var after = await (await reviewer.GetAsync($"{OrgA}/dashboard")).ReadAsync<DashboardDto>();

        Assert.Equal(before.Delivery[DeliveryState.SIMULATED] + 1, after.Delivery[DeliveryState.SIMULATED]);
        Assert.Equal(before.Delivery[DeliveryState.PUBLISHED], after.Delivery[DeliveryState.PUBLISHED]);
        Assert.Equal(before.Editorial[EditorialState.APPROVED] + 1, after.Editorial[EditorialState.APPROVED]); // editorial state stays APPROVED
        Assert.Equal(Enum.GetValues<DeliveryState>().Length, after.Delivery.Count);
        Assert.Contains(after.Destinations, d => d.ExecutionMode == ExecutionMode.NOT_CONFIGURED);
        Assert.Equal(after.PendingReviewCount, (await (await reviewer.GetAsync($"{OrgA}/review-requests?status=PENDING")).ReadAsync<List<ReviewSummaryDto>>()).Count);
    }

    [Fact]
    public async Task Dashboard_is_tenant_scoped()
    {
        await SimulatedPublicationAsync();
        var adminB = await factory.SignInAsync(DemoData.AdminB);
        await (await adminB.GetAsync($"{OrgA}/dashboard")).AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);

        var dashboardB = await (await adminB.GetAsync($"{OrgB}/dashboard")).ReadAsync<DashboardDto>();
        await using var db = factory.CreateDbContext();
        Assert.Equal(await db.PublicationJobs.CountAsync(j => j.OrganizationId == DemoData.OrganizationBId), dashboardB.Delivery.Values.Sum());
        Assert.All(dashboardB.Destinations, d => Assert.DoesNotContain(d.Id, db.SocialAccounts.Where(a => a.OrganizationId == DemoData.OrganizationAId).Select(a => a.Id)));
    }

    [Fact]
    public async Task Calendar_filters_by_date_range_platform_and_state()
    {
        var linkedIn = await SimulatedPublicationAsync("LinkedIn");
        var facebook = await SimulatedPublicationAsync("Facebook");
        var creator = await factory.SignInAsync(DemoData.CreatorA);
        var from = Uri.EscapeDataString(DateTime.UtcNow.AddHours(-1).ToString("O"));
        var to = Uri.EscapeDataString(DateTime.UtcNow.AddHours(1).ToString("O"));

        var range = await (await creator.GetAsync($"{OrgA}/publication-jobs?from={from}&to={to}")).ReadAsync<List<PublicationJobDto>>();
        Assert.Contains(range, j => j.Id == linkedIn.Id);
        Assert.True(range.Zip(range.Skip(1)).All(p => p.First.PublishAtUtc <= p.Second.PublishAtUtc)); // soonest first for a range

        var onlyFacebook = await (await creator.GetAsync($"{OrgA}/publication-jobs?from={from}&to={to}&platform=Facebook&state=SIMULATED")).ReadAsync<List<PublicationJobDto>>();
        Assert.Contains(onlyFacebook, j => j.Id == facebook.Id);
        Assert.All(onlyFacebook, j => Assert.Equal((SocialPlatform.Facebook, DeliveryState.SIMULATED), (j.Platform, j.DeliveryState)));

        var past = Uri.EscapeDataString(DateTime.UtcNow.AddDays(-30).ToString("O"));
        var pastEnd = Uri.EscapeDataString(DateTime.UtcNow.AddDays(-29).ToString("O"));
        Assert.DoesNotContain(await (await creator.GetAsync($"{OrgA}/publication-jobs?from={past}&to={pastEnd}")).ReadAsync<List<PublicationJobDto>>(),
            j => j.Id == linkedIn.Id);
        await (await creator.GetAsync($"{OrgA}/publication-jobs?from={to}&to={from}")).AssertErrorAsync(HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
    }
}
