using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SocialMediaAutomation.Application.Operations;
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

/// <summary>Security, abuse and observability controls of the running API (TASKS.md Phases 26-28).</summary>
[Collection(ApiCollection.Name)]
public class HardeningTests(ApiFactory factory)
{
    private static readonly string OrgA = $"/api/v1/organizations/{DemoData.OrganizationAId}";
    private static readonly string OrgB = $"/api/v1/organizations/{DemoData.OrganizationBId}";

    private static Task<HttpResponseMessage> PostWithETagAsync(HttpClient client, string url, object? body, byte[] rowVersion) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body ?? new { }, options: ApiClient.Json),
            Headers = { { "If-Match", ApiClient.ETag(rowVersion) } },
        });

    // ---- Headers, correlation, anti-forgery ------------------------------------------------------------

    [Fact]
    public async Task Every_response_carries_a_correlation_id_and_security_headers()
    {
        var client = await factory.SignInAsync(DemoData.CreatorA);
        var response = await client.GetAsync($"{OrgA}/posts");

        Assert.True(response.Headers.TryGetValues("X-Correlation-Id", out var ids) && ids.Single().Length >= 8);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());

        // A well-formed caller id is echoed (for tracing across services); anything else is replaced, never reflected.
        var traced = new HttpRequestMessage(HttpMethod.Get, $"{OrgA}/posts") { Headers = { { "X-Correlation-Id", "abc-12345678" } } };
        Assert.Equal("abc-12345678", (await client.SendAsync(traced)).Headers.GetValues("X-Correlation-Id").Single());
        var hostile = new HttpRequestMessage(HttpMethod.Get, $"{OrgA}/posts");
        hostile.Headers.TryAddWithoutValidation("X-Correlation-Id", "<script>alert(1)</script>");
        Assert.DoesNotContain("<script>", (await client.SendAsync(hostile)).Headers.GetValues("X-Correlation-Id").Single());
    }

    [Fact]
    public async Task Problem_responses_include_the_correlation_id()
    {
        var client = await factory.SignInAsync(DemoData.CreatorA);
        var response = await client.GetAsync($"{OrgA}/posts/{Guid.NewGuid()}");
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(response.Headers.GetValues("X-Correlation-Id").Single(), body.GetProperty("correlationId").GetString());
        Assert.Equal(ErrorCodes.NotFound, body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task State_changing_requests_from_a_foreign_origin_are_rejected()
    {
        var client = await factory.SignInAsync(DemoData.CreatorA);
        HttpRequestMessage Create(string? origin)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{OrgA}/posts") { Content = JsonContent.Create(new { Platform = "LinkedIn", Body = "Hello" }, options: ApiClient.Json) };
            if (origin is not null)
                request.Headers.Add("Origin", origin);
            return request;
        }

        await (await client.SendAsync(Create("https://evil.example"))).AssertErrorAsync(HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(Create("http://web.test"))).StatusCode); // App:WebBaseUrl
        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(Create(null))).StatusCode);              // non-browser client
        var read = new HttpRequestMessage(HttpMethod.Get, $"{OrgA}/posts") { Headers = { { "Origin", "https://evil.example" } } };
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(read)).StatusCode); // reads change nothing (and no CORS is enabled)
    }

    [Fact]
    public async Task Session_cookie_is_http_only_and_same_site_strict()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { DemoData.CreatorA.Email, Password = factory.DemoPassword });
        var cookie = login.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("sma_session=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(factory.DemoPassword, await login.Content.ReadAsStringAsync());
    }

    // ---- Rate limiting -----------------------------------------------------------------------------

    [Fact]
    public async Task Sign_in_attempts_are_rate_limited_with_a_stable_code()
    {
        using var limited = factory.WithWebHostBuilder(builder => builder.UseSetting("RateLimiting:LoginPerMinute", "3"));
        var client = limited.CreateClient();
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/login", new { Email = "nobody@demo.example", Password = "wrong" })).StatusCode);

        var blocked = await client.PostAsJsonAsync("/api/v1/auth/login", new { DemoData.CreatorA.Email, Password = factory.DemoPassword });
        await blocked.AssertErrorAsync(HttpStatusCode.TooManyRequests, "RATE_LIMITED");
        Assert.True(blocked.Headers.Contains("Retry-After"));
    }

    // ---- Secrets -----------------------------------------------------------------------------------

    [Fact]
    public async Task Passwords_provider_tokens_oauth_codes_and_llm_keys_never_reach_the_logs()
    {
        const string llmKey = "sk-hardening-secret-0000LOGS";
        var adminB = await factory.SignInAsync(DemoData.AdminB);
        var saved = await adminB.PutJsonAsync($"{OrgB}/ai-settings", new { Provider = "OpenAI", Model = "gpt-test-mini", ApiKey = llmKey },
            (await adminB.GetAsync($"{OrgB}/ai-settings")).StatusCode == HttpStatusCode.OK
                ? ApiClient.ETag((await (await adminB.GetAsync($"{OrgB}/ai-settings")).ReadAsync<AiSettingsRow>()).RowVersion)
                : null);
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
        await adminB.PostAsJsonAsync($"{OrgB}/ai-settings/models", new { });

        var adminA = await factory.SignInAsync(DemoData.AdminA);
        var code = $"logcheck{Guid.NewGuid():N}";
        var start = await (await adminA.PostAsJsonAsync($"{OrgA}/social-connections/LinkedIn/start", new { Mode = "LIVE" })).ReadAsync<Dictionary<string, string>>();
        var state = Uri.UnescapeDataString(new Uri(start["authorizationUrl"]).Query.Split("state=")[1]);
        await factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false })
            .GetAsync($"/api/v1/oauth/linkedin/callback?state={Uri.EscapeDataString(state)}&code={code}");

        var logs = factory.Logs.All;
        Assert.Contains("/api/v1/oauth/linkedin/callback", logs); // the request itself was logged...
        foreach (var secret in new[] { factory.DemoPassword, llmKey, code, state, FakeOAuthGateway.AccessToken(code), FakeOAuthGateway.RefreshToken(code), "Bearer " })
            Assert.DoesNotContain(secret, logs); // ...but none of its secrets
    }

    private sealed record AiSettingsRow(byte[] RowVersion);

    // ---- Workspace suspension ------------------------------------------------------------------------

    /// <summary>An approved simulated post with a Publish Now job that no worker has processed yet.</summary>
    private async Task<PublicationJobDto> DueJobAsync()
    {
        var creator = await factory.SignInAsync(DemoData.CreatorA);
        var account = (await (await creator.GetAsync($"{OrgA}/social-accounts?platform=LinkedIn")).ReadAsync<List<SocialAccountDto>>())
            .First(a => a.ExecutionMode == ExecutionMode.SIMULATED);
        var draft = await (await creator.CreatePostAsync(OrgA,
            new { Platform = "LinkedIn", Hook = "Hook", Body = "Close the books in days.", SocialAccountId = account.Id })).ReadAsync<PostDto>();
        var review = await (await PostWithETagAsync(creator, $"{OrgA}/posts/{draft.Id}/submit-review", null, draft.RowVersion)).ReadAsync<ReviewSummaryDto>();
        await PostWithETagAsync(await factory.SignInAsync(DemoData.ReviewerA), $"{OrgA}/review-requests/{review.Id}/approve", null, review.RowVersion);
        var post = await (await creator.GetAsync($"{OrgA}/posts/{draft.Id}")).ReadAsync<PostDto>();
        var job = await (await PostWithETagAsync(creator, $"{OrgA}/posts/{post.Id}/publish-now", new { PostVersionId = post.CurrentVersionId }, post.RowVersion))
            .ReadAsync<PublicationJobDto>();

        return job;
    }

    [Fact]
    public async Task Suspended_workspace_blocks_new_dispatch()
    {
        var job = await DueJobAsync();

        await using (var db = factory.CreateDbContext())
            await db.Database.ExecuteSqlRawAsync("UPDATE Organizations SET Status = 'Suspended' WHERE Id = {0}", DemoData.OrganizationAId);
        try
        {
            await factory.DrainPublicationsAsync();
        }
        finally
        {
            await using var db = factory.CreateDbContext();
            await db.Database.ExecuteSqlRawAsync("UPDATE Organizations SET Status = 'Active' WHERE Id = {0}", DemoData.OrganizationAId);
        }

        await using var check = factory.CreateDbContext();
        var stored = await check.PublicationJobs.SingleAsync(j => j.Id == job.Id);
        Assert.Equal((DeliveryState.ACTION_REQUIRED, PublicationErrorCodes.WorkspaceInactive), (stored.DeliveryState, stored.LastErrorCode));
    }

    // ---- Health and operations ---------------------------------------------------------------------

    [Fact]
    public async Task Liveness_and_readiness_are_separate_and_readiness_reports_silent_workers()
    {
        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/health/live")).StatusCode);

        await using (var db = factory.CreateDbContext())
            await db.Database.ExecuteSqlRawAsync("DELETE FROM WorkerHeartbeats");
        var degraded = await anonymous.GetAsync("/health/ready");
        Assert.Equal((HttpStatusCode.OK, "Degraded"), (degraded.StatusCode, await degraded.Content.ReadAsStringAsync()));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var heartbeats = scope.ServiceProvider.GetRequiredService<IWorkerHeartbeats>();
            await heartbeats.BeatAsync("test-host:1:ai", WorkerKind.AI, DateTime.UtcNow, CancellationToken.None);
            await heartbeats.BeatAsync("test-host:1:publishing", WorkerKind.PUBLISHING, DateTime.UtcNow, CancellationToken.None);
            await heartbeats.BeatAsync("test-host:1:publishing", WorkerKind.PUBLISHING, DateTime.UtcNow, CancellationToken.None); // upsert, not duplicate
        }
        Assert.Equal("Healthy", await (await anonymous.GetAsync("/health/ready")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Operations_view_shows_workers_queue_age_expired_leases_and_items_needing_people_to_admins_only()
    {
        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IWorkerHeartbeats>().BeatAsync("ops-test:1:publishing", WorkerKind.PUBLISHING, DateTime.UtcNow, CancellationToken.None);
        var admin = await factory.SignInAsync(DemoData.AdminA);
        var before = await (await admin.GetAsync($"{OrgA}/operations")).ReadAsync<OperationsDto>();

        // One job left PROCESSING with an expired lease (a worker died), one needing a person.
        var jobs = new[] { (await DueJobAsync()).Id, (await DueJobAsync()).Id };
        await using (var db = factory.CreateDbContext())
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE PublicationJobs SET DeliveryState = 'PROCESSING', LeaseExpiresAtUtc = DATEADD(minute, -1, SYSUTCDATETIME()), CompletedAtUtc = NULL WHERE Id = {0}", jobs[0]);
            await db.Database.ExecuteSqlRawAsync("UPDATE PublicationJobs SET DeliveryState = 'ACTION_REQUIRED' WHERE Id = {0}", jobs[1]);
        }
        var after = await (await admin.GetAsync($"{OrgA}/operations")).ReadAsync<OperationsDto>();

        Assert.Contains(after.Workers, w => w.WorkerId == "ops-test:1:publishing" && !w.IsStale);
        Assert.Equal(before.Publications.ExpiredLeases + 1, after.Publications.ExpiredLeases);
        Assert.Equal(before.Publications.ActionRequired + 1, after.Publications.ActionRequired);

        await (await (await factory.SignInAsync(DemoData.CreatorA)).GetAsync($"{OrgA}/operations")).AssertErrorAsync(HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        await (await (await factory.SignInAsync(DemoData.AdminB)).GetAsync($"{OrgA}/operations")).AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }
}
