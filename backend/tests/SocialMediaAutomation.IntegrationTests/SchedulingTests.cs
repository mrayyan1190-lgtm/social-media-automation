using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using SocialMediaAutomation.Application.Audit.DTOs;
using SocialMediaAutomation.Application.Campaigns.DTOs;
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

/// <summary>Schedule, Publish Now, cancel and reschedule through the real API and SQL Server. No provider is ever called.</summary>
[Collection(ApiCollection.Name)]
public class SchedulingTests(ApiFactory factory)
{
    private static readonly string OrgA = $"/api/v1/organizations/{DemoData.OrganizationAId}";
    private static readonly string OrgB = $"/api/v1/organizations/{DemoData.OrganizationBId}";
    private static readonly DateOnly Tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

    // ---- Fixtures ----------------------------------------------------------------------------------

    private static Task<HttpResponseMessage> PostWithETagAsync(HttpClient client, string url, object? body, byte[] rowVersion) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body ?? new { }, options: ApiClient.Json),
            Headers = { { "If-Match", ApiClient.ETag(rowVersion) } },
        });

    private static async Task<PostDto> GetPostAsync(HttpClient client, Guid postId) =>
        await (await client.GetAsync($"{OrgA}/posts/{postId}")).ReadAsync<PostDto>();

    private static object Content(Guid destination, string body) => new
    {
        Hook = "Month-end, minus the pain", Body = body, Hashtags = new[] { "FinanceOps" }, SocialAccountId = destination,
    };

    private async Task<(HttpClient Creator, PostDto Post)> DraftAsync(Guid? destination = null)
    {
        var creator = await factory.SignInAsync(DemoData.CreatorA);
        destination ??= (await (await creator.GetAsync($"{OrgA}/social-accounts?platform=LinkedIn")).ReadAsync<List<SocialAccountDto>>())
            .First(a => a.ExecutionMode == ExecutionMode.SIMULATED).Id;
        var campaign = await creator.CreateCampaignAsync(OrgA);
        var post = await (await creator.CreatePostAsync($"{OrgA}/campaigns/{campaign.Id}", new
        {
            Platform = "LinkedIn", Hook = "Month-end, minus the pain", Body = "Close the books in days, not weeks.", SocialAccountId = destination,
        })).ReadAsync<PostDto>();
        return (creator, post);
    }

    /// <summary>Creator submits, a distinct Reviewer approves. Returns the approved post as the creator sees it.</summary>
    private async Task<(HttpClient Creator, PostDto Post, ReviewSummaryDto Approval)> ApprovedAsync(Guid? destination = null)
    {
        var (creator, draft) = await DraftAsync(destination);
        var review = await (await PostWithETagAsync(creator, $"{OrgA}/posts/{draft.Id}/submit-review", null, draft.RowVersion)).ReadAsync<ReviewSummaryDto>();
        var reviewer = await factory.SignInAsync(DemoData.ReviewerA);
        var approval = await (await PostWithETagAsync(reviewer, $"{OrgA}/review-requests/{review.Id}/approve", null, review.RowVersion)).ReadAsync<ReviewSummaryDto>();
        return (creator, await GetPostAsync(creator, draft.Id), approval);
    }

    private static object At(PostDto post, DateOnly date, int hour, int minute, string zone, int? offset = null) => new
    {
        PostVersionId = post.CurrentVersionId, LocalDate = date.ToString("yyyy-MM-dd"), LocalTime = $"{hour:00}:{minute:00}:00", TimeZone = zone,
        UtcOffsetMinutes = offset,
    };

    private static Task<HttpResponseMessage> ScheduleAsync(HttpClient client, PostDto post, object body, string org = "") =>
        PostWithETagAsync(client, $"{(org.Length > 0 ? org : OrgA)}/posts/{post.Id}/schedule", body, post.RowVersion);

    private static Task<HttpResponseMessage> PublishNowAsync(HttpClient client, PostDto post) =>
        PostWithETagAsync(client, $"{OrgA}/posts/{post.Id}/publish-now", new { PostVersionId = post.CurrentVersionId }, post.RowVersion);

    /// <summary>The next nth Sunday of a month that is still ahead (always within the one-year scheduling horizon).</summary>
    private static DateOnly NextSunday(int month, int nth)
    {
        for (var year = DateTime.UtcNow.Year; ; year++)
        {
            var first = new DateOnly(year, month, 1);
            var sunday = first.AddDays(((int)DayOfWeek.Sunday - (int)first.DayOfWeek + 7) % 7 + 7 * (nth - 1));
            if (sunday > DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)))
                return sunday;
        }
    }

    private async Task<int> JobCountAsync(Guid postId)
    {
        await using var db = factory.CreateDbContext();
        return await db.PublicationJobs.CountAsync(j => j.PostId == postId);
    }

    private async Task ForceStateAsync(Guid jobId, DeliveryState state)
    {
        await using var db = factory.CreateDbContext();
        await db.PublicationJobs.Where(j => j.Id == jobId).ExecuteUpdateAsync(s => s.SetProperty(j => j.DeliveryState, state));
    }

    // ---- Approval gate -----------------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Acceptance")]
    [Trait("Acceptance", "08 unapproved schedule rejected")]
    public async Task Unapproved_content_cannot_be_scheduled_or_published_now()
    {
        var (creator, draft) = await DraftAsync();
        await (await ScheduleAsync(creator, draft, At(draft, Tomorrow, 9, 0, "Europe/London"))).AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.ApprovalRequired);
        await (await PublishNowAsync(creator, draft)).AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.ApprovalRequired);

        await PostWithETagAsync(creator, $"{OrgA}/posts/{draft.Id}/submit-review", null, draft.RowVersion);
        var inReview = await GetPostAsync(creator, draft.Id);
        await (await PublishNowAsync(creator, inReview)).AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.ApprovalRequired);
        Assert.Equal(0, await JobCountAsync(draft.Id));
    }

    [Fact]
    public async Task Stale_approval_is_rejected()
    {
        var (creator, approved, _) = await ApprovedAsync();

        // The client names a version that is not the approved current one.
        await (await ScheduleAsync(creator, approved, new { PostVersionId = Guid.NewGuid(), LocalDate = Tomorrow.ToString("yyyy-MM-dd"), LocalTime = "09:00:00", TimeZone = "UTC" }))
            .AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.ApprovalStale);

        // Edited after approval: the old ETag is stale, and the new draft is no longer approved.
        var edited = await (await PostWithETagAsync(creator, $"{OrgA}/posts/{approved.Id}/versions",
            Content(approved.CurrentVersion.SocialAccountId!.Value, "Edited after approval."), approved.RowVersion)).ReadAsync<PostDto>();
        await (await ScheduleAsync(creator, approved, At(approved, Tomorrow, 9, 0, "UTC"))).AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.ConcurrencyConflict);
        await (await ScheduleAsync(creator, edited, At(edited, Tomorrow, 9, 0, "UTC"))).AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.ApprovalRequired);
        Assert.Equal(0, await JobCountAsync(approved.Id));
    }

    // ---- Scheduling and idempotency ----------------------------------------------------------------

    [Fact]
    [Trait("Category", "Acceptance")]
    [Trait("Acceptance", "09 duplicate schedule one intent")]
    public async Task Schedule_creates_one_durable_intent_and_repeated_clicks_never_duplicate_it()
    {
        var (creator, post, approval) = await ApprovedAsync();
        var body = At(post, Tomorrow, 9, 30, "America/New_York");

        var first = await ScheduleAsync(creator, post, body);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var job = await first.ReadAsync<PublicationJobDto>();

        Assert.Equal((DeliveryState.SCHEDULED, ExecutionMode.SIMULATED, post.CurrentVersionId, approval.ApprovedPayloadHash),
            (job.DeliveryState, job.ExecutionMode, job.PostVersionId, job.PayloadHash));
        Assert.Equal(("America/New_York", Tomorrow.ToDateTime(new TimeOnly(9, 30))), (job.TimeZoneId, job.LocalPublishAt!.Value));
        Assert.Equal(job.LocalPublishAt.Value.AddMinutes(-job.UtcOffsetMinutes!.Value), job.PublishAtUtc);
        Assert.Null(job.ExternalPostId);

        // Same request again, sequentially and concurrently: always the same single intent.
        var again = await ScheduleAsync(creator, post, body);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(job.Id, (await again.ReadAsync<PublicationJobDto>()).Id);
        var burst = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => ScheduleAsync(creator, post, body)));
        foreach (var response in burst)
            Assert.Equal(job.Id, (await response.ReadAsync<PublicationJobDto>()).Id);
        Assert.Equal(1, await JobCountAsync(post.Id));

        // A different time for the same intent must go through reschedule.
        await (await ScheduleAsync(creator, post, At(post, Tomorrow, 11, 0, "America/New_York")))
            .AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.IdempotencyConflict);

        var audit = await (await (await factory.SignInAsync(DemoData.AdminA)).GetAsync($"{OrgA}/audit?resourceId={job.Id}")).ReadAsync<List<AuditLogDto>>();
        Assert.Single(audit, a => a.Action == AuditActions.PublicationScheduled);
    }

    [Fact]
    [Trait("Category", "Acceptance")]
    [Trait("Acceptance", "09 duplicate schedule one intent")]
    public async Task Concurrent_first_schedules_create_exactly_one_job()
    {
        var (creator, post, _) = await ApprovedAsync();
        var body = At(post, Tomorrow, 14, 0, "Europe/London");
        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => ScheduleAsync(creator, post, body)));

        var ids = new HashSet<Guid>();
        foreach (var response in responses)
            ids.Add((await response.ReadAsync<PublicationJobDto>()).Id);
        Assert.Single(ids);
        Assert.Equal(1, await JobCountAsync(post.Id));
    }

    [Fact]
    public async Task Dst_gaps_and_overlaps_are_handled_explicitly()
    {
        var (creator, post, _) = await ApprovedAsync();
        // New York: 02:30 on the second Sunday of March is skipped; 01:30 on the first Sunday of November happens twice.
        var springForward = NextSunday(3, nth: 2);
        var fallBack = NextSunday(11, nth: 1);
        await (await ScheduleAsync(creator, post, At(post, springForward, 2, 30, "America/New_York")))
            .AssertErrorAsync(HttpStatusCode.BadRequest, ErrorCodes.LocalTimeNonexistent);
        await (await ScheduleAsync(creator, post, At(post, fallBack, 1, 30, "America/New_York")))
            .AssertErrorAsync(HttpStatusCode.BadRequest, ErrorCodes.LocalTimeAmbiguous);
        await (await ScheduleAsync(creator, post, At(post, Tomorrow, 9, 0, "Eastern Standard Time")))
            .AssertErrorAsync(HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        await (await ScheduleAsync(creator, post, At(post, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), 9, 0, "UTC")))
            .AssertErrorAsync(HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        Assert.Equal(0, await JobCountAsync(post.Id));

        var job = await (await ScheduleAsync(creator, post, At(post, fallBack, 1, 30, "America/New_York", offset: -300)))
            .ReadAsync<PublicationJobDto>();
        Assert.Equal(fallBack.ToDateTime(new TimeOnly(6, 30)), job.PublishAtUtc); // the second (standard-time) occurrence
        Assert.Equal(-300, job.UtcOffsetMinutes);
    }

    [Fact]
    public async Task Publish_now_creates_an_immediately_due_job_for_the_worker()
    {
        var (creator, post, approval) = await ApprovedAsync();
        var before = DateTime.UtcNow.AddSeconds(-5);

        var response = await PublishNowAsync(creator, post);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = await response.ReadAsync<PublicationJobDto>();

        // Only a durable, due job exists; nothing was dispatched in the request.
        Assert.Equal((DeliveryState.SCHEDULED, (DateTime?)null, (string?)null, approval.ApprovedPayloadHash),
            (job.DeliveryState, job.LocalPublishAt, job.ExternalPostId, job.PayloadHash));
        Assert.InRange(job.PublishAtUtc, before, DateTime.UtcNow.AddSeconds(5));
        await using (var db = factory.CreateDbContext())
        {
            var stored = await db.PublicationJobs.SingleAsync(j => j.Id == job.Id);
            Assert.True(stored.NextActionAtUtc <= DateTime.UtcNow);
            Assert.Equal((0, (Guid?)null), (stored.AttemptCount, stored.LeaseToken));
        }

        Assert.Equal(job.Id, (await (await PublishNowAsync(creator, post)).ReadAsync<PublicationJobDto>()).Id);
        Assert.Equal(1, await JobCountAsync(post.Id));
    }

    [Fact]
    public async Task Publish_now_moves_an_existing_schedule_forward_instead_of_duplicating()
    {
        var (creator, post, _) = await ApprovedAsync();
        var scheduled = await (await ScheduleAsync(creator, post, At(post, Tomorrow, 9, 0, "UTC"))).ReadAsync<PublicationJobDto>();
        var now = await (await PublishNowAsync(creator, post)).ReadAsync<PublicationJobDto>();

        Assert.Equal(scheduled.Id, now.Id);
        Assert.True(now.PublishAtUtc < scheduled.PublishAtUtc);
        Assert.Null(now.TimeZoneId);
        Assert.Equal(1, await JobCountAsync(post.Id));
    }

    // ---- Permissions and tenancy -------------------------------------------------------------------

    [Fact]
    public async Task Reviewers_and_other_workspaces_cannot_schedule()
    {
        var (_, post, _) = await ApprovedAsync();
        var reviewer = await factory.SignInAsync(DemoData.ReviewerA);
        await (await ScheduleAsync(reviewer, post, At(post, Tomorrow, 9, 0, "UTC"))).AssertErrorAsync(HttpStatusCode.Forbidden, ErrorCodes.Forbidden);

        var adminB = await factory.SignInAsync(DemoData.AdminB);
        await (await ScheduleAsync(adminB, post, At(post, Tomorrow, 9, 0, "UTC"))).AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
        await (await ScheduleAsync(adminB, post, At(post, Tomorrow, 9, 0, "UTC"), OrgB)).AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
        Assert.Equal(0, await JobCountAsync(post.Id));
    }

    [Fact]
    public async Task Cross_tenant_destination_and_jobs_are_rejected()
    {
        var adminB = await factory.SignInAsync(DemoData.AdminB);
        var accountB = (await (await adminB.GetAsync($"{OrgB}/social-accounts")).ReadAsync<List<SocialAccountDto>>()).First();

        // A post in workspace A cannot target workspace B's destination.
        var creator = await factory.SignInAsync(DemoData.CreatorA);
        var campaign = await creator.CreateCampaignAsync(OrgA);
        var foreign = await creator.CreatePostAsync($"{OrgA}/campaigns/{campaign.Id}",
            new { Platform = "LinkedIn", Hook = "Hook", Body = "Body", SocialAccountId = accountB.Id });
        Assert.False(foreign.IsSuccessStatusCode);

        // Workspace B can neither see nor cancel workspace A's publication.
        var (creatorA, post, _) = await ApprovedAsync();
        var job = await (await ScheduleAsync(creatorA, post, At(post, Tomorrow, 9, 0, "UTC"))).ReadAsync<PublicationJobDto>();
        await (await adminB.GetAsync($"{OrgB}/publication-jobs/{job.Id}")).AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
        await (await PostWithETagAsync(adminB, $"{OrgB}/publication-jobs/{job.Id}/cancel", null, job.RowVersion))
            .AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
        Assert.DoesNotContain(await (await adminB.GetAsync($"{OrgB}/publication-jobs")).ReadAsync<List<PublicationJobDto>>(), j => j.Id == job.Id);
    }

    [Fact]
    public async Task Disconnected_live_destination_cannot_be_scheduled()
    {
        var admin = await factory.SignInAsync(DemoData.AdminA);
        var code = $"u{Guid.NewGuid():N}";
        var start = await (await admin.PostAsJsonAsync($"{OrgA}/social-connections/LinkedIn/start", new { Mode = "LIVE" })).ReadAsync<Dictionary<string, string>>();
        var state = Uri.UnescapeDataString(new Uri(start["authorizationUrl"]).Query.Split("state=")[1]);
        await factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false })
            .GetAsync($"/api/v1/oauth/linkedin/callback?state={Uri.EscapeDataString(state)}&code={code}");
        var live = (await (await admin.GetAsync($"{OrgA}/social-accounts")).ReadAsync<List<SocialAccountDto>>()).Single(a => a.ExternalAccountId == $"acct-{code}");

        var (creator, post, _) = await ApprovedAsync(live.Id);
        var connection = (await (await admin.GetAsync($"{OrgA}/social-connections")).ReadAsync<List<SocialConnectionDto>>()).Single(c => c.DisplayName == $"User {code}");
        await admin.PostAsync($"{OrgA}/social-connections/{connection.Id}/disconnect", null);

        await (await PublishNowAsync(creator, post)).AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.SocialAccountNotReady);
        Assert.Equal(0, await JobCountAsync(post.Id));
    }

    // ---- Cancel, reschedule, edit after scheduling --------------------------------------------------

    [Fact]
    public async Task Scheduled_job_can_be_canceled_and_rescheduled_but_not_once_dispatch_started()
    {
        var (creator, post, _) = await ApprovedAsync();
        var job = await (await ScheduleAsync(creator, post, At(post, Tomorrow, 9, 0, "UTC"))).ReadAsync<PublicationJobDto>();
        var reviewer = await factory.SignInAsync(DemoData.ReviewerA);
        await (await PostWithETagAsync(reviewer, $"{OrgA}/publication-jobs/{job.Id}/cancel", null, job.RowVersion))
            .AssertErrorAsync(HttpStatusCode.Forbidden, ErrorCodes.Forbidden);

        var moved = await (await PostWithETagAsync(creator, $"{OrgA}/publication-jobs/{job.Id}/reschedule",
            new { LocalDate = Tomorrow.AddDays(1).ToString("yyyy-MM-dd"), LocalTime = "10:15:00", TimeZone = "Europe/London" }, job.RowVersion))
            .ReadAsync<PublicationJobDto>();
        Assert.Equal((job.Id, DeliveryState.SCHEDULED, "Europe/London"), (moved.Id, moved.DeliveryState, moved.TimeZoneId));
        await (await PostWithETagAsync(creator, $"{OrgA}/publication-jobs/{job.Id}/cancel", null, job.RowVersion))
            .AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.ConcurrencyConflict);

        var canceled = await (await PostWithETagAsync(creator, $"{OrgA}/publication-jobs/{job.Id}/cancel", null, moved.RowVersion)).ReadAsync<PublicationJobDto>();
        Assert.Equal((DeliveryState.CANCELED, false, true), (canceled.DeliveryState, canceled.CanCancel, canceled.CanReschedule));
        await (await PostWithETagAsync(creator, $"{OrgA}/publication-jobs/{job.Id}/cancel", null, canceled.RowVersion))
            .AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.InvalidStateTransition);

        // Scheduling again reactivates the same lifetime intent rather than creating another.
        var reactivated = await (await ScheduleAsync(creator, post, At(post, Tomorrow, 12, 0, "UTC"))).ReadAsync<PublicationJobDto>();
        Assert.Equal((job.Id, DeliveryState.SCHEDULED), (reactivated.Id, reactivated.DeliveryState));

        // Once the worker owns it, nothing may cancel or move it.
        await ForceStateAsync(job.Id, DeliveryState.PROCESSING);
        var inFlight = await (await creator.GetAsync($"{OrgA}/publication-jobs/{job.Id}")).ReadAsync<PublicationJobDto>();
        await (await PostWithETagAsync(creator, $"{OrgA}/publication-jobs/{job.Id}/cancel", null, inFlight.RowVersion))
            .AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.PublicationTooLateToCancel);
        await (await PostWithETagAsync(creator, $"{OrgA}/publication-jobs/{job.Id}/reschedule",
            new { LocalDate = Tomorrow.ToString("yyyy-MM-dd"), LocalTime = "15:00:00", TimeZone = "UTC" }, inFlight.RowVersion))
            .AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.PublicationTooLateToCancel);
        Assert.Equal(1, await JobCountAsync(post.Id));
    }

    [Fact]
    public async Task Editing_scheduled_content_cancels_the_safe_job_but_an_in_flight_job_blocks_the_edit()
    {
        var (creator, post, _) = await ApprovedAsync();
        var job = await (await ScheduleAsync(creator, post, At(post, Tomorrow, 9, 0, "UTC"))).ReadAsync<PublicationJobDto>();

        var edited = await (await PostWithETagAsync(creator, $"{OrgA}/posts/{post.Id}/versions",
            Content(post.CurrentVersion.SocialAccountId!.Value, "Edited while scheduled."), post.RowVersion)).ReadAsync<PostDto>();
        Assert.Equal(EditorialState.DRAFT, edited.EditorialState);
        Assert.Equal(DeliveryState.CANCELED, (await (await creator.GetAsync($"{OrgA}/publication-jobs/{job.Id}")).ReadAsync<PublicationJobDto>()).DeliveryState);

        var (creator2, other, _) = await ApprovedAsync();
        var running = await (await PublishNowAsync(creator2, other)).ReadAsync<PublicationJobDto>();
        await ForceStateAsync(running.Id, DeliveryState.PROCESSING);
        await (await PostWithETagAsync(creator2, $"{OrgA}/posts/{other.Id}/versions",
            Content(other.CurrentVersion.SocialAccountId!.Value, "Too late to edit."), other.RowVersion))
            .AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.PublicationTooLateToCancel);
        var unchanged = await GetPostAsync(creator2, other.Id);
        Assert.Equal((EditorialState.APPROVED, other.CurrentVersionId), (unchanged.EditorialState, unchanged.CurrentVersionId));
    }

    [Fact]
    public async Task Upcoming_list_shows_open_jobs_only()
    {
        var (creator, post, _) = await ApprovedAsync();
        var job = await (await ScheduleAsync(creator, post, At(post, Tomorrow, 9, 0, "UTC"))).ReadAsync<PublicationJobDto>();
        Assert.Contains(await (await creator.GetAsync($"{OrgA}/publication-jobs?openOnly=true")).ReadAsync<List<PublicationJobDto>>(), j => j.Id == job.Id);

        await PostWithETagAsync(creator, $"{OrgA}/publication-jobs/{job.Id}/cancel", null, job.RowVersion);
        Assert.DoesNotContain(await (await creator.GetAsync($"{OrgA}/publication-jobs?openOnly=true")).ReadAsync<List<PublicationJobDto>>(), j => j.Id == job.Id);
        Assert.Contains(await (await creator.GetAsync($"{OrgA}/publication-jobs?postId={post.Id}")).ReadAsync<List<PublicationJobDto>>(), j => j.Id == job.Id);
    }
}
