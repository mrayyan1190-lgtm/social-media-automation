using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.Audit.DTOs;
using SocialMediaAutomation.Application.Media.DTOs;
using SocialMediaAutomation.Application.Notifications.DTOs;
using SocialMediaAutomation.Application.Posts.DTOs;
using SocialMediaAutomation.Application.Publications.DTOs;
using SocialMediaAutomation.Application.Reviews.DTOs;
using SocialMediaAutomation.Application.SocialAccounts.DTOs;
using SocialMediaAutomation.Application.Tests;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Infrastructure.Persistence.Seed;
using SocialMediaAutomation.IntegrationTests.Infrastructure;

namespace SocialMediaAutomation.IntegrationTests;

/// <summary>
/// The durable publishing engine against SQL Server: claiming, leases, revalidation, outcomes and recovery. The real
/// SimulatedPublisher runs; live providers are scripted fakes (the real adapters are tested against stub HTTP).
/// </summary>
[Collection(ApiCollection.Name)]
public class PublishingTests : IDisposable
{
    private static readonly string OrgA = $"/api/v1/organizations/{DemoData.OrganizationAId}";
    private readonly ApiFactory factory;

    public PublishingTests(ApiFactory factory)
    {
        this.factory = factory;
        factory.Publishing.Reset();
    }

    public void Dispose() => factory.Publishing.Reset();

    // ---- Fixtures ----------------------------------------------------------------------------------

    private static Task<HttpResponseMessage> PostWithETagAsync(HttpClient client, string url, object? body, byte[] rowVersion) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body ?? new { }, options: ApiClient.Json),
            Headers = { { "If-Match", ApiClient.ETag(rowVersion) } },
        });

    /// <summary>A connected LIVE destination created through the domain (the OAuth round trip is covered elsewhere).</summary>
    private async Task<Guid> LiveAccountAsync(SocialPlatform platform, string[] capabilities, DateTime? tokenExpiresAtUtc = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var secrets = scope.ServiceProvider.GetRequiredService<ISecretProtector>();
        var id = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        var connection = OAuthConnection.Create(Guid.NewGuid(), DemoData.OrganizationAId, platform, $"user-{id}", "Live tester", DemoData.AdminA.Id, now);
        connection.Grant(secrets.Protect($"live-token-{id}"), null, tokenExpiresAtUtc ?? now.AddDays(30), ["scope"], "Live tester", DemoData.AdminA.Id, now);
        var external = platform == SocialPlatform.LinkedIn ? $"urn:li:person:{id}" : $"acct-{id}";
        var account = SocialAccount.CreateConnected(Guid.NewGuid(), connection, external, $"{platform} live {id[..6]}", ExecutionMode.LIVE, capabilities, DemoData.AdminA.Id, now);
        await using var db = factory.CreateDbContext();
        db.Add(connection);
        db.Add(account);
        await db.SaveChangesAsync();
        return account.Id;
    }

    private async Task<Guid> SimulatedAccountAsync(HttpClient client, string platform) =>
        (await (await client.GetAsync($"{OrgA}/social-accounts?platform={platform}")).ReadAsync<List<SocialAccountDto>>())
        .First(a => a.ExecutionMode == ExecutionMode.SIMULATED).Id;

    /// <summary>Creator drafts (with an image for Instagram) and submits; a distinct Reviewer approves.</summary>
    private async Task<(HttpClient Creator, PostDto Post)> ApprovedAsync(string platform = "LinkedIn", Guid? destination = null)
    {
        var creator = await factory.SignInAsync(DemoData.CreatorA);
        destination ??= await SimulatedAccountAsync(creator, platform);
        Guid? mediaId = null;
        if (platform == "Instagram")
        {
            var file = new ByteArrayContent(ImageProbeTests.JpegHeader(1080, 1080));
            file.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");
            mediaId = (await (await creator.PostAsync($"{OrgA}/media", new MultipartFormDataContent { { file, "file", "square.jpg" } }))
                .ReadAsync<MediaAssetDto>()).Id;
        }
        var draft = await (await creator.CreatePostAsync(OrgA, new
        {
            Platform = platform, Hook = "Month-end, minus the pain", Body = "Close the books in days, not weeks.", Hashtags = new[] { "FinanceOps" },
            Images = mediaId is null ? null : new[] { new { MediaAssetId = mediaId.Value, AltText = "Dashboard screenshot" } }, SocialAccountId = destination,
        })).ReadAsync<PostDto>();
        var review = await (await PostWithETagAsync(creator, $"{OrgA}/posts/{draft.Id}/submit-review", null, draft.RowVersion)).ReadAsync<ReviewSummaryDto>();
        var reviewer = await factory.SignInAsync(DemoData.ReviewerA);
        await (await PostWithETagAsync(reviewer, $"{OrgA}/review-requests/{review.Id}/approve", null, review.RowVersion)).ReadAsync<ReviewSummaryDto>();
        return (creator, await (await creator.GetAsync($"{OrgA}/posts/{draft.Id}")).ReadAsync<PostDto>());
    }

    private async Task<(HttpClient Creator, PublicationJobDto Job)> PublishNowAsync(string platform = "LinkedIn", Guid? destination = null)
    {
        var (creator, post) = await ApprovedAsync(platform, destination);
        var job = await (await PostWithETagAsync(creator, $"{OrgA}/posts/{post.Id}/publish-now", new { PostVersionId = post.CurrentVersionId }, post.RowVersion))
            .ReadAsync<PublicationJobDto>();
        return (creator, job);
    }

    private async Task<PublicationJob> StoredAsync(Guid jobId)
    {
        await using var db = factory.CreateDbContext();
        return await db.PublicationJobs.AsNoTracking().SingleAsync(j => j.Id == jobId);
    }

    private async Task<List<PublicationAttempt>> AttemptsAsync(Guid jobId)
    {
        await using var db = factory.CreateDbContext();
        return await db.PublicationAttempts.AsNoTracking().Where(a => a.PublicationJobId == jobId).OrderBy(a => a.StartedAtUtc).ToListAsync();
    }

    private async Task UpdateJobAsync(Guid jobId, string setSql)
    {
        await using var db = factory.CreateDbContext();
        var sql = "UPDATE PublicationJobs SET " + setSql + " WHERE Id = {0}"; // setSql is a test-authored constant
        await db.Database.ExecuteSqlRawAsync(sql, jobId);
    }

    private Task MakeDueAsync(Guid jobId) => UpdateJobAsync(jobId, "NextActionAtUtc = DATEADD(second, -1, SYSUTCDATETIME())");

    private Task ExpireLeaseAsync(Guid jobId) => UpdateJobAsync(jobId, "LeaseExpiresAtUtc = DATEADD(second, -1, SYSUTCDATETIME())");

    // ---- Simulation ------------------------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Acceptance")]
    [Trait("Acceptance", "15 SIMULATED never PUBLISHED")]
    public async Task Simulation_runs_through_the_worker_and_ends_SIMULATED_without_provider_evidence()
    {
        var (creator, job) = await PublishNowAsync();
        Assert.Equal(DeliveryState.SCHEDULED, job.DeliveryState); // nothing happens in the request

        await factory.DrainPublicationsAsync();

        var done = await (await creator.GetAsync($"{OrgA}/publication-jobs/{job.Id}")).ReadAsync<PublicationJobDto>();
        Assert.Equal((DeliveryState.SIMULATED, (string?)null, (string?)null), (done.DeliveryState, done.ExternalPostId, done.ExternalPostUrl));
        var attempt = Assert.Single(await (await creator.GetAsync($"{OrgA}/publication-jobs/{job.Id}/attempts")).ReadAsync<List<PublicationAttemptDto>>());
        Assert.Equal((DeliveryState.SIMULATED, (string?)null), (attempt.ResultState, attempt.ExternalId));
        Assert.Contains("Nothing was sent to LinkedIn", attempt.Detail);
        Assert.Equal(0, factory.Publishing.Calls.Count(c => c.Context.JobId == job.Id));

        var notifications = await (await creator.GetAsync($"{OrgA}/notifications")).ReadAsync<List<NotificationDto>>();
        Assert.Contains(notifications, n => n.Type == NotificationType.SIMULATED && n.ResourceId == job.Id);
        var audit = await (await (await factory.SignInAsync(DemoData.AdminA)).GetAsync($"{OrgA}/audit?resourceId={job.Id}")).ReadAsync<List<AuditLogDto>>();
        Assert.Contains(audit, a => a.Action == AuditActions.PublicationSimulated);
        Assert.DoesNotContain(audit, a => a.Action == AuditActions.PublicationPublished);
    }

    [Fact]
    [Trait("Category", "Acceptance")]
    [Trait("Acceptance", "16 browser closure does not stop execution")]
    public async Task Scheduled_job_runs_only_when_due_independent_of_any_browser()
    {
        var (creator, post) = await ApprovedAsync();
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        var job = await (await PostWithETagAsync(creator, $"{OrgA}/posts/{post.Id}/schedule",
            new { PostVersionId = post.CurrentVersionId, LocalDate = tomorrow.ToString("yyyy-MM-dd"), LocalTime = "09:00:00", TimeZone = "UTC" }, post.RowVersion))
            .ReadAsync<PublicationJobDto>();
        creator.Dispose(); // the browser session is gone; the job lives in SQL

        await factory.DrainPublicationsAsync();
        Assert.Equal(DeliveryState.SCHEDULED, (await StoredAsync(job.Id)).DeliveryState);

        await MakeDueAsync(job.Id);
        await factory.DrainPublicationsAsync();
        Assert.Equal(DeliveryState.SIMULATED, (await StoredAsync(job.Id)).DeliveryState);
    }

    // ---- Live path and ownership -----------------------------------------------------------------------

    [Fact]
    public async Task Live_publish_stores_provider_evidence_and_never_exposes_the_token()
    {
        var account = await LiveAccountAsync(SocialPlatform.LinkedIn, ["publish_text"]);
        var (creator, job) = await PublishNowAsync(destination: account);
        Assert.Equal(ExecutionMode.LIVE, job.ExecutionMode);

        await factory.DrainPublicationsAsync();

        var done = await (await creator.GetAsync($"{OrgA}/publication-jobs/{job.Id}")).ReadAsync<PublicationJobDto>();
        Assert.Equal((DeliveryState.PUBLISHED, $"ext-{job.Id:N}"), (done.DeliveryState, done.ExternalPostId));
        Assert.Equal($"https://provider.test/posts/{job.Id:N}", done.ExternalPostUrl);
        var attempt = Assert.Single(await AttemptsAsync(job.Id));
        Assert.Equal(("req-123", $"ext-{job.Id:N}", DeliveryState.PUBLISHED), (attempt.ProviderRequestId, attempt.ExternalId, attempt.ResultState));

        var context = factory.Publishing.Calls.Single(c => c.Context.JobId == job.Id && c.Step == "publish").Context;
        Assert.StartsWith("live-token-", context.AccessToken);
        Assert.DoesNotContain("live-token-", context.ToString());
        Assert.DoesNotContain("live-token-", await creator.GetStringAsync($"{OrgA}/publication-jobs/{job.Id}/attempts"));
        Assert.Equal(1, factory.Publishing.CallsFor(job.Id, "publish"));
    }

    [Fact]
    [Trait("Category", "Acceptance")]
    [Trait("Acceptance", "10 two workers cannot own a job")]
    public async Task Two_workers_never_own_or_publish_the_same_job()
    {
        var account = await LiveAccountAsync(SocialPlatform.LinkedIn, ["publish_text"]);
        var jobs = new List<Guid>();
        for (var i = 0; i < 4; i++)
            jobs.Add((await PublishNowAsync(destination: account)).Job.Id);

        // Concurrent claims hand out disjoint jobs.
        var claims = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => factory.ClaimPublicationsAsync($"worker-{i}")));
        var mine = claims.SelectMany(c => c).Where(c => jobs.Contains(c.JobId)).ToList();
        Assert.Equal(mine.Count, mine.Select(c => c.JobId).Distinct().Count());

        // Then several workers race to process everything (including the claimed jobs, via lease expiry).
        foreach (var job in jobs)
            await ExpireLeaseAsync(job);
        await Task.WhenAll(factory.DrainPublicationsAsync("racer-1"), factory.DrainPublicationsAsync("racer-2"), factory.DrainPublicationsAsync("racer-3"));

        foreach (var job in jobs)
        {
            Assert.Equal(DeliveryState.PUBLISHED, (await StoredAsync(job)).DeliveryState);
            Assert.Equal(1, factory.Publishing.CallsFor(job, "publish"));
            Assert.Single(await AttemptsAsync(job), a => a.ResultState == DeliveryState.PUBLISHED);
        }
    }

    [Fact]
    public async Task Stale_worker_cannot_overwrite_newer_state_or_send_after_losing_its_lease()
    {
        var account = await LiveAccountAsync(SocialPlatform.LinkedIn, ["publish_text"]);
        var (_, job) = await PublishNowAsync(destination: account);
        var (_, staleToken) = Assert.Single(await factory.ClaimPublicationsAsync("stale-worker"), c => c.JobId == job.Id);

        // While the stale worker is between claim and dispatch checkpoint, its lease expires and another worker finishes the job.
        factory.Publishing.OnValidate = async context =>
        {
            if (context.JobId != job.Id || factory.Publishing.CallsFor(job.Id, "validate") > 1)
                return;
            await ExpireLeaseAsync(job.Id);
            await factory.DrainPublicationsAsync("fresh-worker");
        };
        await factory.ProcessPublicationAsync(job.Id, staleToken, "stale-worker");

        var stored = await StoredAsync(job.Id);
        Assert.Equal(DeliveryState.PUBLISHED, stored.DeliveryState);
        Assert.Equal(1, factory.Publishing.CallsFor(job.Id, "publish")); // the stale worker never sent
        Assert.All(await AttemptsAsync(job.Id), a => Assert.Equal("fresh-worker", a.WorkerId));

        // Replaying the stale token later is a no-op.
        await factory.ProcessPublicationAsync(job.Id, staleToken, "stale-worker");
        Assert.Equal(stored.RowVersion, (await StoredAsync(job.Id)).RowVersion);
    }

    [Fact]
    [Trait("Category", "Acceptance")]
    [Trait("Acceptance", "11 restart before dispatch recovers")]
    public async Task Worker_crash_before_dispatch_is_recovered_after_lease_expiry()
    {
        var account = await LiveAccountAsync(SocialPlatform.LinkedIn, ["publish_text"]);
        var (_, job) = await PublishNowAsync(destination: account);
        Assert.Contains(await factory.ClaimPublicationsAsync("crashed-worker"), c => c.JobId == job.Id); // claimed, then the process dies

        await factory.DrainPublicationsAsync();
        Assert.Equal(DeliveryState.PROCESSING, (await StoredAsync(job.Id)).DeliveryState); // lease still valid: nobody steals it

        await ExpireLeaseAsync(job.Id);
        await factory.DrainPublicationsAsync();

        var stored = await StoredAsync(job.Id);
        Assert.Equal((DeliveryState.PUBLISHED, 2), (stored.DeliveryState, stored.AttemptCount));
        Assert.Equal(1, factory.Publishing.CallsFor(job.Id, "publish"));
    }

    // ---- Failure categories ----------------------------------------------------------------------------

    [Fact]
    public async Task Safe_transient_failures_retry_with_backoff_and_stop_at_the_attempt_limit()
    {
        var account = await LiveAccountAsync(SocialPlatform.LinkedIn, ["publish_text"]);
        var (_, job) = await PublishNowAsync(destination: account);
        var throttle = true;
        factory.Publishing.Publish = context => throttle && context.JobId == job.Id
            ? throw new PublishFailure(PublishErrorCategory.RETRYABLE, "THROTTLED", "LinkedIn: rate limited", "req-429")
            : PublisherScript.Success(context);

        await factory.DrainPublicationsAsync();
        var waiting = await StoredAsync(job.Id);
        Assert.Equal((DeliveryState.RETRY_PENDING, "THROTTLED", (DateTime?)null), (waiting.DeliveryState, waiting.LastErrorCode, waiting.DispatchStartedAtUtc));
        Assert.True(waiting.NextActionAtUtc > DateTime.UtcNow.AddSeconds(30));

        throttle = false;
        await MakeDueAsync(job.Id);
        await factory.DrainPublicationsAsync();
        Assert.Equal(DeliveryState.PUBLISHED, (await StoredAsync(job.Id)).DeliveryState);
        Assert.Equal([DeliveryState.RETRY_PENDING, DeliveryState.PUBLISHED], (await AttemptsAsync(job.Id)).Select(a => a.ResultState!.Value));

        // Always throttled: bounded by MaxAttempts, then FAILED.
        var (_, bounded) = await PublishNowAsync(destination: account);
        factory.Publishing.Publish = _ => throw new PublishFailure(PublishErrorCategory.RETRYABLE, "THROTTLED", "rate limited");
        for (var i = 0; i < PublicationJob.DefaultMaxAttempts + 1; i++)
        {
            await MakeDueAsync(bounded.Id);
            await factory.DrainPublicationsAsync();
        }
        var failed = await StoredAsync(bounded.Id);
        Assert.Equal((DeliveryState.FAILED, PublicationJob.DefaultMaxAttempts), (failed.DeliveryState, failed.AttemptCount));
        Assert.Equal(PublicationJob.DefaultMaxAttempts, factory.Publishing.CallsFor(bounded.Id, "publish"));
    }

    [Fact]
    [Trait("Category", "Acceptance")]
    [Trait("Acceptance", "12 timeout after dispatch reconciles")]
    public async Task Uncertain_outcomes_are_reconciled_and_never_resent()
    {
        var account = await LiveAccountAsync(SocialPlatform.LinkedIn, ["publish_text"]);
        var (creator, timeout) = await PublishNowAsync(destination: account);
        var (_, crash) = await PublishNowAsync(destination: account);
        factory.Publishing.Publish = context =>
            context.JobId == timeout.Id ? throw new PublishFailure(PublishErrorCategory.UNCERTAIN, "TIMEOUT_AFTER_SEND", "no answer after send")
            : context.JobId == crash.Id ? throw new InvalidOperationException("adapter bug after the request went out")
            : PublisherScript.Success(context);

        await factory.DrainPublicationsAsync();
        await MakeDueAsync(timeout.Id);
        await factory.DrainPublicationsAsync();

        var reconcile = await (await creator.GetAsync($"{OrgA}/publication-jobs/{timeout.Id}")).ReadAsync<PublicationJobDto>();
        Assert.Equal((DeliveryState.RECONCILIATION_REQUIRED, "TIMEOUT_AFTER_SEND", false), (reconcile.DeliveryState, reconcile.LastErrorCode, reconcile.CanCancel));
        Assert.Equal(PublicationErrorCodes.UnexpectedAfterSend, (await StoredAsync(crash.Id)).LastErrorCode);
        Assert.Equal(DeliveryState.RECONCILIATION_REQUIRED, (await StoredAsync(crash.Id)).DeliveryState);
        Assert.Equal(1, factory.Publishing.CallsFor(timeout.Id, "publish"));
        Assert.Equal(1, factory.Publishing.CallsFor(crash.Id, "publish"));

        await (await PostWithETagAsync(creator, $"{OrgA}/publication-jobs/{timeout.Id}/cancel", null, reconcile.RowVersion))
            .AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.PublicationTooLateToCancel);
        var notifications = await (await creator.GetAsync($"{OrgA}/notifications")).ReadAsync<List<NotificationDto>>();
        Assert.Contains(notifications, n => n.Type == NotificationType.RECONCILIATION_REQUIRED && n.ResourceId == timeout.Id);
    }

    [Fact]
    public async Task Lease_lost_after_dispatch_is_reconciled_and_keeps_the_provider_evidence()
    {
        var account = await LiveAccountAsync(SocialPlatform.LinkedIn, ["publish_text"]);
        var (_, job) = await PublishNowAsync(destination: account);
        factory.Publishing.Publish = async context =>
        {
            if (context.JobId == job.Id)
            {
                // The provider is slow; meanwhile the lease expires and the sweep settles the job.
                await ExpireLeaseAsync(job.Id);
                await factory.SweepPublicationsAsync();
            }
            return await PublisherScript.Success(context);
        };

        await factory.DrainPublicationsAsync();

        var stored = await StoredAsync(job.Id);
        Assert.Equal((DeliveryState.RECONCILIATION_REQUIRED, PublicationErrorCodes.LeaseLostAfterDispatch), (stored.DeliveryState, stored.LastErrorCode));
        Assert.Contains(await AttemptsAsync(job.Id), a => a.ExternalId == $"ext-{job.Id:N}" && a.ErrorCode == PublicationErrorCodes.PersistenceFailedAfterSend);
        await factory.DrainPublicationsAsync();
        Assert.Equal(1, factory.Publishing.CallsFor(job.Id, "publish"));
    }

    [Fact]
    public async Task Provider_success_followed_by_a_failed_save_is_reconciled_with_the_external_id()
    {
        var account = await LiveAccountAsync(SocialPlatform.LinkedIn, ["publish_text"]);
        var (_, job) = await PublishNowAsync(destination: account);
        factory.Publishing.Publish = async context =>
        {
            if (context.JobId == job.Id)
                await UpdateJobAsync(job.Id, "UpdatedAtUtc = SYSUTCDATETIME()"); // the row changes under us, so the outcome save fails
            return await PublisherScript.Success(context);
        };

        await factory.DrainPublicationsAsync();

        var stored = await StoredAsync(job.Id);
        Assert.Equal((DeliveryState.RECONCILIATION_REQUIRED, PublicationErrorCodes.PersistenceFailedAfterSend, $"ext-{job.Id:N}"),
            (stored.DeliveryState, stored.LastErrorCode, stored.ExternalPostId));
        Assert.Equal(1, factory.Publishing.CallsFor(job.Id, "publish"));
    }

    [Fact]
    [Trait("Category", "Acceptance")]
    [Trait("Acceptance", "13 expired authorization requires action")]
    public async Task Expired_or_rejected_authorization_requires_action_and_is_not_retried()
    {
        var expiredAccount = await LiveAccountAsync(SocialPlatform.LinkedIn, ["publish_text"], tokenExpiresAtUtc: DateTime.UtcNow.AddDays(1));
        var (creator, expired) = await PublishNowAsync(destination: expiredAccount);
        await using (var db = factory.CreateDbContext())
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE c SET AccessTokenExpiresAtUtc = DATEADD(minute, -1, SYSUTCDATETIME()) FROM OAuthConnections c JOIN SocialAccounts a ON a.OAuthConnectionId = c.Id WHERE a.Id = {0}",
                expiredAccount);

        var rejectedAccount = await LiveAccountAsync(SocialPlatform.LinkedIn, ["publish_text"]);
        var (_, rejected) = await PublishNowAsync(destination: rejectedAccount);
        factory.Publishing.Publish = context => context.JobId == rejected.Id
            ? throw new PublishFailure(PublishErrorCategory.ACTION_REQUIRED, "AUTH_INVALID", "LinkedIn: token revoked. Reconnect the destination.")
            : PublisherScript.Success(context);

        await factory.DrainPublicationsAsync();
        await MakeDueAsync(expired.Id);
        await MakeDueAsync(rejected.Id);
        await factory.DrainPublicationsAsync();

        var expiredJob = await StoredAsync(expired.Id);
        Assert.Equal((DeliveryState.ACTION_REQUIRED, PublicationErrorCodes.AuthExpired, 1), (expiredJob.DeliveryState, expiredJob.LastErrorCode, expiredJob.AttemptCount));
        Assert.Equal(0, factory.Publishing.CallsFor(expired.Id, "publish")); // caught by revalidation, the provider was never called
        var rejectedJob = await StoredAsync(rejected.Id);
        Assert.Equal((DeliveryState.ACTION_REQUIRED, "AUTH_INVALID", 1), (rejectedJob.DeliveryState, rejectedJob.LastErrorCode, rejectedJob.AttemptCount));
        Assert.Equal(1, factory.Publishing.CallsFor(rejected.Id, "publish"));

        var notifications = await (await creator.GetAsync($"{OrgA}/notifications")).ReadAsync<List<NotificationDto>>();
        Assert.Contains(notifications, n => n.Type == NotificationType.ACTION_REQUIRED && n.ResourceId == expired.Id);
    }

    [Fact]
    public async Task Revalidation_blocks_dispatch_when_destination_payload_or_timing_is_no_longer_valid()
    {
        var account = await LiveAccountAsync(SocialPlatform.LinkedIn, ["publish_text"]);
        var (_, disconnected) = await PublishNowAsync(destination: account);
        await using (var db = factory.CreateDbContext())
            await db.Database.ExecuteSqlRawAsync("UPDATE SocialAccounts SET ConnectionStatus = 'DISCONNECTED' WHERE Id = {0}", account);

        var (_, tampered) = await PublishNowAsync();
        await UpdateJobAsync(tampered.Id, "PayloadJson = REPLACE(PayloadJson, 'Close the books', 'Tampered text')");

        var (_, late) = await PublishNowAsync();
        await UpdateJobAsync(late.Id, "PublishAtUtc = DATEADD(hour, -3, SYSUTCDATETIME())");

        await factory.DrainPublicationsAsync();

        Assert.Equal((DeliveryState.ACTION_REQUIRED, ErrorCodes.SocialAccountNotReady),
            ((await StoredAsync(disconnected.Id)).DeliveryState, (await StoredAsync(disconnected.Id)).LastErrorCode));
        Assert.Equal((DeliveryState.ACTION_REQUIRED, ErrorCodes.ApprovalStale),
            ((await StoredAsync(tampered.Id)).DeliveryState, (await StoredAsync(tampered.Id)).LastErrorCode));
        Assert.Equal((DeliveryState.ACTION_REQUIRED, PublicationErrorCodes.TooLate),
            ((await StoredAsync(late.Id)).DeliveryState, (await StoredAsync(late.Id)).LastErrorCode));
        Assert.Equal(0, factory.Publishing.CallsFor(disconnected.Id, "publish"));
    }

    // ---- Instagram multi-step ----------------------------------------------------------------------------

    [Fact]
    public async Task Instagram_container_checkpoint_survives_restarts_and_is_published_once()
    {
        var account = await LiveAccountAsync(SocialPlatform.Instagram, ["publish_image"]);
        var (_, job) = await PublishNowAsync("Instagram", account);
        factory.Publishing.Status = _ => MediaContainerStatus.InProgress;

        await factory.DrainPublicationsAsync();
        var waiting = await StoredAsync(job.Id);
        Assert.Equal((DeliveryState.WAITING_MEDIA, $"container-{job.Id:N}", (Guid?)null), (waiting.DeliveryState, waiting.ProviderContainerId, waiting.LeaseToken));
        Assert.NotNull(waiting.MediaContainerCreatedAtUtc);
        Assert.True(waiting.NextActionAtUtc > DateTime.UtcNow);

        // A fresh worker run before the next check is due does nothing; when due it only polls.
        await factory.DrainPublicationsAsync();
        await MakeDueAsync(job.Id);
        await factory.DrainPublicationsAsync();
        Assert.Equal(DeliveryState.WAITING_MEDIA, (await StoredAsync(job.Id)).DeliveryState);

        // A worker claims the next check and dies; after lease expiry another worker resumes from the stored container.
        await MakeDueAsync(job.Id);
        Assert.Contains(await factory.ClaimPublicationsAsync("crashed-worker"), c => c.JobId == job.Id);
        await ExpireLeaseAsync(job.Id);
        factory.Publishing.Status = _ => MediaContainerStatus.Ready;
        await factory.DrainPublicationsAsync();

        var done = await StoredAsync(job.Id);
        Assert.Equal((DeliveryState.PUBLISHED, $"ext-{job.Id:N}", 1), (done.DeliveryState, done.ExternalPostId, done.AttemptCount));
        Assert.Equal(1, factory.Publishing.CallsFor(job.Id, "prepare"));
        Assert.Equal(1, factory.Publishing.CallsFor(job.Id, "publish"));
        Assert.Equal($"container-{job.Id:N}", factory.Publishing.Calls.Single(c => c.Context.JobId == job.Id && c.Step == "publish").Context.ContainerId);
        Assert.Contains(await AttemptsAsync(job.Id), a => a.Step == PublicationAttemptStep.MEDIA_STATUS && a.ResultState == DeliveryState.WAITING_MEDIA);
    }

    [Fact]
    public async Task Instagram_media_that_never_finishes_processing_requires_action()
    {
        var account = await LiveAccountAsync(SocialPlatform.Instagram, ["publish_image"]);
        var (_, job) = await PublishNowAsync("Instagram", account);
        factory.Publishing.Status = _ => MediaContainerStatus.InProgress;
        await factory.DrainPublicationsAsync();

        await UpdateJobAsync(job.Id, "MediaContainerCreatedAtUtc = DATEADD(minute, -31, SYSUTCDATETIME())");
        await MakeDueAsync(job.Id);
        await factory.DrainPublicationsAsync();

        var stored = await StoredAsync(job.Id);
        Assert.Equal((DeliveryState.ACTION_REQUIRED, PublicationErrorCodes.MediaProcessingTimeout), (stored.DeliveryState, stored.LastErrorCode));
        Assert.Equal(0, factory.Publishing.CallsFor(job.Id, "publish"));
    }
}
