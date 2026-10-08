using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SocialMediaAutomation.Application.AiGeneration.Services;
using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.AiGeneration.DTOs;
using SocialMediaAutomation.Application.Organizations.DTOs;
using SocialMediaAutomation.Application.Posts.DTOs;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Infrastructure.Persistence.Seed;
using SocialMediaAutomation.IntegrationTests.Infrastructure;

namespace SocialMediaAutomation.IntegrationTests;

/// <summary>
/// End-to-end AI pipeline against SQL Server with a scripted LLM: API enqueues, the worker processor claims and
/// processes, and results land as posts, immutable versions, evaluations and call logs.
/// </summary>
[Collection(ApiCollection.Name)]
public class AiGenerationTests(ApiFactory factory)
{
    private const string ApiKey = "sk-test-0123456789abcdWXYZ";
    private static readonly string OrgA = $"/api/v1/organizations/{DemoData.OrganizationAId}";

    // ---- Scripted model responses ---------------------------------------------------------------

    private const string Prompt = "Launch posts for Brightline Close. 40 finance teams run their month-end close on Brightline. Ask readers to book a demo.";

    private static string Variant(string platform, string body, string hook = "Month-end without the scramble", string[]? hashtags = null) =>
        JsonSerializer.Serialize(new { platform, language = "en", hook, body, cta = "Book a demo", hashtags = hashtags ?? ["FinanceOps"] });

    private static string Generation(string? linkedInBody = null) => $$"""
        {"schemaVersion":"content-v2","variants":[
          {{Variant("LinkedIn", linkedInBody ?? "Finance leaders: month-end close no longer has to mean spreadsheets. 40 teams already run their close on Brightline.")}},
          {{Variant("Facebook", "Tired of month-end chaos? Brightline brings your numbers together so you can wrap up faster.", hook: "Month-end, minus the chaos")}},
          {{Variant("Instagram", "Close faster.\nStress less.\nOne set of numbers.", hook: "Your month-end, simplified ✨", hashtags: ["FinanceOps", "Brightline", "MonthEnd"])}}
        ]}
        """;

    private static string Evaluation(params (string Platform, string? UnsupportedClaim)[] posts) => JsonSerializer.Serialize(new
    {
        evaluations = posts.Select(p => new
        {
            platform = p.Platform, brandAlignment = 4, audienceRelevance = 5, clarity = 4, platformFit = 5, ctaRelevance = 4,
            summary = "On brand and clear.",
            claims = p.UnsupportedClaim is null ? [] : new[] { new { text = p.UnsupportedClaim, kind = "statistic", supported = false, contradictsFacts = false } },
            issues = new[] { new { severity = "CRITICAL", category = "tone", message = "A bit formal.", excerpt = (string?)null } },
        }),
    });

    private void Script(Func<LlmChatRequest, string> generation, Func<LlmChatRequest, string>? evaluation = null) =>
        factory.Llm.Handler = (_, request) => FakeLlmClient.Reply(FakeLlmClient.IsEvaluation(request)
            ? (evaluation ?? (_ => Evaluation(("LinkedIn", null), ("Facebook", null), ("Instagram", null))))(request)
            : generation(request));

    // ---- Fixtures ----------------------------------------------------------------------------------

    private async Task<HttpClient> EnsureAiConfiguredAsync()
    {
        var admin = await factory.SignInAsync(DemoData.AdminA);
        var existing = await admin.GetAsync($"{OrgA}/ai-settings");
        var body = new { Provider = "OpenAI", Model = "gpt-test-mini", ApiKey };
        var saved = existing.StatusCode == HttpStatusCode.NotFound
            ? await admin.PutJsonAsync($"{OrgA}/ai-settings", body, ifMatch: null)
            : await admin.PutJsonAsync($"{OrgA}/ai-settings", body, ApiClient.ETag((await existing.ReadAsync<AiSettingsDto>()).RowVersion));
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
        return admin;
    }

    /// <summary>AI configured and a unique prompt tag, so a test never picks up another test's still-active request.</summary>
    private async Task<(HttpClient Creator, string Tag)> SetupAsync()
    {
        await EnsureAiConfiguredAsync();
        return (await factory.SignInAsync(DemoData.CreatorA), Guid.NewGuid().ToString("N")[..8]);
    }

    private static object GenerationBody(string tag) =>
        new { Prompt = $"{Prompt} Ref {tag}.", Platforms = new[] { "LinkedIn", "Facebook", "Instagram" } };

    /// <summary>The drafts the request wrote, from the workspace post list.</summary>
    private static async Task<List<PostListItemDto>> DraftsOfAsync(HttpClient client, Guid requestId)
    {
        var ids = (await GetRequestAsync(client, requestId)).Outputs.Select(o => o.PostId).ToHashSet();
        return (await PostsAsync(client)).Where(p => ids.Contains(p.Id)).ToList();
    }

    private static async Task<List<PostListItemDto>> PostsAsync(HttpClient client) =>
        await (await client.GetAsync($"{OrgA}/posts")).ReadAsync<List<PostListItemDto>>();

    private static async Task<AiGenerationRequestDto> RequestGenerationAsync(HttpClient client, string tag)
    {
        var response = await client.PostAsJsonAsync($"{OrgA}/ai-generations", GenerationBody(tag));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return await response.ReadAsync<AiGenerationRequestDto>();
    }

    private static async Task<AiGenerationRequestDto> GetRequestAsync(HttpClient client, Guid requestId) =>
        await (await client.GetAsync($"{OrgA}/ai-generations/{requestId}")).ReadAsync<AiGenerationRequestDto>();

    private async Task MakeDueAsync(Guid requestId)
    {
        await using var db = factory.CreateDbContext();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE AiGenerationRequests SET NextAttemptAtUtc = SYSUTCDATETIME() WHERE Id = {requestId}");
    }

    // ---- Durable request -------------------------------------------------------------------------------

    [Fact]
    public async Task Generation_request_is_durable_and_returns_before_any_llm_call()
    {
        var (creator, tag) = await SetupAsync();
        var callsBefore = factory.Llm.Calls.Count;

        var queued = await RequestGenerationAsync(creator, tag);
        var again = await RequestGenerationAsync(creator, tag);

        Assert.Equal(AiRequestStatus.PENDING, queued.Status);
        Assert.Equal(callsBefore, factory.Llm.Calls.Count); // the HTTP request never waited on the model
        Assert.Equal(queued.Id, again.Id); // a repeated click returns the same active request
        await using (var db = factory.CreateDbContext())
        {
            var row = await db.AiGenerationRequests.AsNoTracking().SingleAsync(r => r.Id == queued.Id);
            Assert.Contains("40 finance teams", row.InputSnapshot);
            Assert.Contains("Brightline Analytics", row.InputSnapshot);
            Assert.DoesNotContain(ApiKey, row.InputSnapshot);
            Assert.Equal(("generation-v4", "content-v2", "gpt-test-mini"), (row.PromptVersion, row.SchemaVersion, row.Model));
        }

        Script(_ => Generation());
        await factory.DrainAiQueueAsync();
        Assert.Equal(AiRequestStatus.COMPLETED, (await GetRequestAsync(creator, queued.Id)).Status);
    }

    [Fact]
    public async Task Two_workers_cannot_claim_the_same_request()
    {
        var (creator, tag) = await SetupAsync();
        var queued = await RequestGenerationAsync(creator, tag);

        var claims = await Task.WhenAll(Enumerable.Range(0, 8).Select(async i =>
        {
            await using var scope = factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IAiWorkQueue>().TryClaimNextAsync($"racer-{i}", TimeSpan.FromMinutes(5), CancellationToken.None);
        }));

        var winners = claims.Where(c => c?.RequestId == queued.Id).ToList();
        Assert.Single(winners);
        await using (var db = factory.CreateDbContext())
        {
            var row = await db.AiGenerationRequests.AsNoTracking().SingleAsync(r => r.Id == queued.Id);
            Assert.Equal((AiRequestStatus.PROCESSING, 1, winners[0]!.Value.LeaseToken), (row.Status, row.AttemptCount, row.LeaseToken));
        }

        // A stale worker (wrong lease token) changes nothing; the lease holder completes the request.
        Script(_ => Generation());
        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AiGenerationProcessor>().ProcessAsync(queued.Id, Guid.NewGuid(), CancellationToken.None);
        Assert.Equal(AiRequestStatus.PROCESSING, (await GetRequestAsync(creator, queued.Id)).Status);
        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AiGenerationProcessor>().ProcessAsync(queued.Id, winners[0]!.Value.LeaseToken, CancellationToken.None);
        Assert.Equal(AiRequestStatus.COMPLETED, (await GetRequestAsync(creator, queued.Id)).Status);
    }

    // ---- Correct platform creation ----------------------------------------------------------------------

    [Fact]
    public async Task Worker_creates_one_post_with_version_1_per_platform_and_records_metadata()
    {
        var (creator, tag) = await SetupAsync();
        var queued = await RequestGenerationAsync(creator, tag);
        Script(_ => Generation());

        await factory.DrainAiQueueAsync();
        var done = await GetRequestAsync(creator, queued.Id);
        var posts = await DraftsOfAsync(creator, queued.Id);

        Assert.Equal(AiRequestStatus.COMPLETED, done.Status);
        Assert.Equal(3, done.Outputs.Count);
        Assert.Equal([SocialPlatform.LinkedIn, SocialPlatform.Facebook, SocialPlatform.Instagram], posts.Select(p => p.Platform).Order());
        Assert.All(posts, p => Assert.Equal((EditorialState.DRAFT, 1), (p.EditorialState, p.CurrentVersionNumber)));

        var bodies = new List<string>();
        foreach (var post in posts)
        {
            var detail = await (await creator.GetAsync($"{OrgA}/posts/{post.Id}")).ReadAsync<PostDto>();
            Assert.Equal(PostVersionSource.AiGeneration, detail.CurrentVersion.CreationSource);
            Assert.Equal(DemoData.CreatorA.Id, detail.CurrentVersion.CreatedByUserId);
            Assert.Null(detail.CurrentVersion.DestinationUrl); // the model never sets the link; a person adds it in Composer
            bodies.Add(detail.CurrentVersion.Body);
            var evaluation = Assert.Single(await (await creator.GetAsync($"{OrgA}/posts/{post.Id}/evaluations")).ReadAsync<List<ContentEvaluationDto>>());
            Assert.Equal(detail.CurrentVersionId, evaluation.PostVersionId);
            Assert.Equal(4, evaluation.BrandAlignment);
            // The model labelled a tone remark CRITICAL; only factual problems may be critical, so it is capped at WARNING.
            Assert.Contains(evaluation.Issues, i => i is { Category: "TONE", Severity: IssueSeverity.WARNING, Source: IssueSource.AI });
        }
        Assert.Equal(3, bodies.Distinct().Count()); // independently adapted, not one caption copied

        await using var db = factory.CreateDbContext();
        var logs = await db.AiCallLogs.AsNoTracking().Where(l => l.RequestId == queued.Id).ToListAsync();
        Assert.Equal([AiCallPurpose.Generation, AiCallPurpose.Evaluation], logs.Select(l => l.Purpose).Order());
        Assert.All(logs, l => Assert.Equal((true, 1200, 450, "gpt-test-mini"), (l.Succeeded, l.InputTokens, l.OutputTokens, l.Model)));
        Assert.All(factory.Llm.Calls.TakeLast(2), c => Assert.Equal((ApiKey, "https://api.openai.com/v1"), (c.Connection.ApiKey, c.Connection.BaseUrl)));
        Assert.DoesNotContain(ApiKey, factory.Llm.Calls[^1].Request.UserContent + factory.Llm.Calls[^1].Request.SystemPrompt);
    }

    // ---- Malformed output -----------------------------------------------------------------------------

    [Fact]
    public async Task Malformed_output_is_never_persisted_and_fails_after_bounded_retries()
    {
        var (creator, tag) = await SetupAsync();
        var postsBefore = (await PostsAsync(creator)).Count;
        var queued = await RequestGenerationAsync(creator, tag);
        Script(_ => "Sure! Here are your posts: LinkedIn - ...");

        await factory.DrainAiQueueAsync();
        var afterFirst = await GetRequestAsync(creator, queued.Id);
        for (var attempt = 2; attempt <= 3; attempt++)
        {
            await MakeDueAsync(queued.Id);
            await factory.DrainAiQueueAsync();
        }
        var final = await GetRequestAsync(creator, queued.Id);
        var postsAfter = (await PostsAsync(creator)).Count;

        Assert.Equal((AiRequestStatus.PENDING, 1, AiFailureCodes.OutputInvalid), (afterFirst.Status, afterFirst.AttemptCount, afterFirst.FailureCode));
        Assert.Equal((AiRequestStatus.FAILED, 3, AiFailureCodes.OutputInvalid), (final.Status, final.AttemptCount, final.FailureCode));
        Assert.Equal(postsBefore, postsAfter);
        await using var db = factory.CreateDbContext();
        var logs = await db.AiCallLogs.AsNoTracking().Where(l => l.RequestId == queued.Id).ToListAsync();
        Assert.Equal(3, logs.Count);
        Assert.All(logs, l => Assert.False(l.Succeeded));
        Assert.Contains("Here are your posts", logs[0].Diagnostic);
    }

    [Fact]
    public async Task Output_missing_a_requested_platform_is_rejected()
    {
        var (creator, tag) = await SetupAsync();
        var postsBefore = (await PostsAsync(creator)).Count;
        var queued = await RequestGenerationAsync(creator, tag);
        Script(_ => $$"""{"schemaVersion":"content-v2","variants":[{{Variant("LinkedIn", "Only one")}}]}""");

        await factory.DrainAiQueueAsync();
        var result = await GetRequestAsync(creator, queued.Id);

        Assert.Equal(AiFailureCodes.OutputInvalid, result.FailureCode);
        Assert.Contains("Missing variants", result.FailureMessage);
        Assert.Equal(postsBefore, (await PostsAsync(creator)).Count);
        await using var db = factory.CreateDbContext();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE AiGenerationRequests SET Status = 'FAILED' WHERE Id = {queued.Id}");
    }

    // ---- Provider failure ------------------------------------------------------------------------------

    [Fact]
    public async Task Transient_provider_failure_is_retried_and_auth_failure_is_final()
    {
        var (creator, tag) = await SetupAsync();
        var queued = await RequestGenerationAsync(creator, tag);

        factory.Llm.Handler = (_, _) => throw new LlmException(LlmErrorKind.Transient, "HTTP 503", 503, 40, "upstream overloaded");
        await factory.DrainAiQueueAsync();
        var retrying = await GetRequestAsync(creator, queued.Id);

        Script(_ => Generation());
        await MakeDueAsync(queued.Id);
        await factory.DrainAiQueueAsync();
        var recovered = await GetRequestAsync(creator, queued.Id);

        Assert.Equal((AiRequestStatus.PENDING, AiFailureCodes.ProviderUnavailable), (retrying.Status, retrying.FailureCode));
        Assert.Equal((AiRequestStatus.COMPLETED, 2), (recovered.Status, recovered.AttemptCount));

        var (creator2, tag2) = await SetupAsync();
        var second = await RequestGenerationAsync(creator2, tag2);
        factory.Llm.Handler = (_, _) => throw new LlmException(LlmErrorKind.Authentication, "The provider rejected the API key.", 401, 30, null);
        await factory.DrainAiQueueAsync();
        var failed = await GetRequestAsync(creator2, second.Id);

        Assert.Equal((AiRequestStatus.FAILED, 1, AiFailureCodes.ProviderAuth), (failed.Status, failed.AttemptCount, failed.FailureCode));
    }

    // ---- Unsupported claims ----------------------------------------------------------------------------

    [Fact]
    public async Task Unsupported_claims_and_prohibited_terms_become_critical_issues()
    {
        var (creator, tag) = await SetupAsync();
        var queued = await RequestGenerationAsync(creator, tag);
        Script(
            _ => Generation(linkedInBody: "Customers save 50% of reporting time. Results guaranteed."),
            _ => Evaluation(("LinkedIn", "Customers save 50% of reporting time"), ("Facebook", null), ("Instagram", null)));

        await factory.DrainAiQueueAsync();
        var linkedIn = (await GetRequestAsync(creator, queued.Id)).Outputs.Single(o => o.Platform == SocialPlatform.LinkedIn);
        var evaluation = Assert.Single(await (await creator.GetAsync($"{OrgA}/posts/{linkedIn.PostId}/evaluations")).ReadAsync<List<ContentEvaluationDto>>());

        Assert.True(evaluation.HasCriticalIssue);
        Assert.Contains(evaluation.Issues, i => i is { Category: "UNSUPPORTED_CLAIM", Severity: IssueSeverity.CRITICAL, Source: IssueSource.AI });
        Assert.Contains(evaluation.Issues, i => i is { Category: "UNSUPPORTED_CLAIM", Severity: IssueSeverity.WARNING, Source: IssueSource.DETERMINISTIC, Excerpt: "50%" });
        Assert.Contains(evaluation.Issues, i => i is { Category: "PROHIBITED_TERM", Severity: IssueSeverity.CRITICAL, Excerpt: "guaranteed" });
        // The flagged text is kept for the human reviewer; nothing was silently "repaired" with an invented fact.
        var post = await (await creator.GetAsync($"{OrgA}/posts/{linkedIn.PostId}")).ReadAsync<PostDto>();
        Assert.Contains("50%", post.CurrentVersion.Body);
    }

    [Fact]
    [Trait("Category", "Acceptance")]
    [Trait("Acceptance", "04 critical claim blocks review")]
    public async Task Critical_unsupported_claim_alone_blocks_submission_until_a_human_removes_it()
    {
        var (creator, tag) = await SetupAsync();
        var queued = await RequestGenerationAsync(creator, tag);
        Script(_ => Generation());
        await factory.DrainAiQueueAsync();
        var postId = (await DraftsOfAsync(creator, queued.Id))
            .Single(p => p.Platform == SocialPlatform.Facebook).Id;
        var destination = (await (await creator.GetAsync($"{OrgA}/social-accounts?platform=Facebook")).ReadAsync<List<SocialMediaAutomation.Application.SocialAccounts.DTOs.SocialAccountDto>>())
            .First(a => a.ExecutionMode == ExecutionMode.SIMULATED).Id;
        Task<HttpResponseMessage> WithETag(HttpMethod method, string url, object body, byte[] rowVersion) => creator.SendAsync(new HttpRequestMessage(method, url)
        {
            Content = JsonContent.Create(body),
            Headers = { { "If-Match", ApiClient.ETag(rowVersion) } },
        });
        async Task<PostDto> Current() => await (await creator.GetAsync($"{OrgA}/posts/{postId}")).ReadAsync<PostDto>();

        // Give the draft a destination, then let an AI rewrite introduce an unsupported statistic (and nothing else wrong).
        var withDestination = await (await WithETag(HttpMethod.Post, $"{OrgA}/posts/{postId}/versions",
            new { Hook = "Month-end, minus the chaos", Body = "Brightline brings your numbers together.", CallToAction = "Book a demo", Hashtags = new[] { "FinanceOps" }, SocialAccountId = destination },
            (await Current()).RowVersion)).ReadAsync<PostDto>();
        const string claim = "Customers cut month-end time by 70%";
        await WithETag(HttpMethod.Post, $"{OrgA}/posts/{postId}/rewrites", new { Action = "Shorten" }, withDestination.RowVersion);
        Script(_ => $$"""{"schemaVersion":"content-v2","variants":[{{Variant("Facebook", claim + ".", hook: "Month-end, minus the chaos")}}]}""",
            _ => Evaluation(("Facebook", claim)));
        await factory.DrainAiQueueAsync();
        var rewritten = await Current();
        Assert.Equal((3, PostVersionSource.AiRewrite, destination), (rewritten.CurrentVersion.VersionNumber, rewritten.CurrentVersion.CreationSource, rewritten.CurrentVersion.SocialAccountId!.Value));

        var blocked = await WithETag(HttpMethod.Post, $"{OrgA}/posts/{postId}/submit-review", new { }, rewritten.RowVersion);
        await blocked.AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.SubmissionBlocked);
        var reasons = await blocked.Content.ReadAsStringAsync();
        Assert.Contains("Critical issue", reasons);
        Assert.Contains("70%", reasons);
        Assert.DoesNotContain("destination", reasons, StringComparison.OrdinalIgnoreCase);

        // The human removes the claim (new version); the same content minus the claim can be submitted.
        var fixedVersion = await (await WithETag(HttpMethod.Post, $"{OrgA}/posts/{postId}/versions",
            new { Hook = "Month-end, minus the chaos", Body = "Customers close month-end faster.", CallToAction = "Book a demo", Hashtags = new[] { "FinanceOps" }, SocialAccountId = destination },
            rewritten.RowVersion)).ReadAsync<PostDto>();
        Assert.Equal(HttpStatusCode.Created, (await WithETag(HttpMethod.Post, $"{OrgA}/posts/{postId}/submit-review", new { }, fixedVersion.RowVersion)).StatusCode);
    }

    // ---- Rewrite ------------------------------------------------------------------------------------

    [Fact]
    public async Task Rewrite_creates_a_new_version_and_leaves_the_source_version_unchanged()
    {
        var (creator, tag) = await SetupAsync();
        var queued = await RequestGenerationAsync(creator, tag);
        Script(_ => Generation());
        await factory.DrainAiQueueAsync();
        var posts = await DraftsOfAsync(creator, queued.Id);
        var before = await (await creator.GetAsync($"{OrgA}/posts/{posts.Single(p => p.Platform == SocialPlatform.Facebook).Id}")).ReadAsync<PostDto>();

        var rewrite = await creator.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"{OrgA}/posts/{before.Id}/rewrites")
        {
            Content = JsonContent.Create(new { Action = "Shorten" }),
            Headers = { { "If-Match", ApiClient.ETag(before.RowVersion) } },
        });
        Assert.Equal(HttpStatusCode.Accepted, rewrite.StatusCode);
        var rewriteRequest = await rewrite.ReadAsync<AiGenerationRequestDto>();
        string? rewritePrompt = null;
        Script(request => { rewritePrompt = request.SystemPrompt; return $$"""{"schemaVersion":"content-v2","variants":[{{Variant("Facebook", "Month-end, done faster.")}}]}"""; },
            _ => Evaluation(("Facebook", null)));
        await factory.DrainAiQueueAsync();

        var after = await (await creator.GetAsync($"{OrgA}/posts/{before.Id}")).ReadAsync<PostDto>();
        var versions = await (await creator.GetAsync($"{OrgA}/posts/{before.Id}/versions")).ReadAsync<List<PostVersionDto>>();
        var evaluations = await (await creator.GetAsync($"{OrgA}/posts/{before.Id}/evaluations")).ReadAsync<List<ContentEvaluationDto>>();

        Assert.Equal(AiRequestStatus.COMPLETED, (await GetRequestAsync(creator, rewriteRequest.Id)).Status);
        Assert.Contains("shorter", rewritePrompt);
        Assert.Equal((2, PostVersionSource.AiRewrite, "Month-end, done faster."), (after.CurrentVersion.VersionNumber, after.CurrentVersion.CreationSource, after.CurrentVersion.Body));
        Assert.Equal([2, 1], versions.Select(v => v.VersionNumber));
        Assert.Equal((before.CurrentVersionId, before.CurrentVersion.Body), (versions[1].Id, versions[1].Body));
        Assert.Contains(evaluations, e => e.PostVersionId == after.CurrentVersionId); // the rewritten version was re-evaluated
    }

    [Fact]
    public async Task Rewrite_of_a_version_that_was_superseded_meanwhile_fails_without_writing()
    {
        var (creator, tag) = await SetupAsync();
        var generation = await RequestGenerationAsync(creator, tag);
        Script(_ => Generation());
        await factory.DrainAiQueueAsync();
        var postId = (await DraftsOfAsync(creator, generation.Id))[0].Id;
        var post = await (await creator.GetAsync($"{OrgA}/posts/{postId}")).ReadAsync<PostDto>();

        var queued = await (await creator.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"{OrgA}/posts/{postId}/rewrites")
        {
            Content = JsonContent.Create(new { Action = "Custom", CustomInstruction = "Mention the webinar" }),
            Headers = { { "If-Match", ApiClient.ETag(post.RowVersion) } },
        })).ReadAsync<AiGenerationRequestDto>();
        var edited = await (await creator.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"{OrgA}/posts/{postId}/versions")
        {
            Content = JsonContent.Create(new { Body = "Manual edit made while the rewrite was queued.", Hashtags = Array.Empty<string>() }),
            Headers = { { "If-Match", ApiClient.ETag(post.RowVersion) } },
        })).ReadAsync<PostDto>();
        var llmCalls = factory.Llm.Calls.Count;
        await factory.DrainAiQueueAsync();

        var result = await GetRequestAsync(creator, queued.Id);
        var current = await (await creator.GetAsync($"{OrgA}/posts/{postId}")).ReadAsync<PostDto>();
        Assert.Equal((AiRequestStatus.FAILED, AiFailureCodes.SourceChanged), (result.Status, result.FailureCode));
        Assert.Equal(llmCalls, factory.Llm.Calls.Count);
        Assert.Equal(edited.CurrentVersionId, current.CurrentVersionId);
    }

    // ---- Access and configuration ------------------------------------------------------------------

    [Fact]
    public async Task Reviewer_and_other_workspaces_cannot_request_generation()
    {
        var (_, tag) = await SetupAsync();
        var reviewer = await factory.SignInAsync(DemoData.ReviewerA);
        var adminB = await factory.SignInAsync(DemoData.AdminB);

        await (await reviewer.PostAsJsonAsync($"{OrgA}/ai-generations", GenerationBody(tag)))
            .AssertErrorAsync(HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        // A member of another workspace cannot generate drafts into this one.
        await (await adminB.PostAsJsonAsync($"{OrgA}/ai-generations", GenerationBody(tag)))
            .AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
        await (await adminB.GetAsync($"{OrgA}/ai-settings")).AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Generation_without_configured_provider_is_rejected_up_front()
    {
        var adminB = await factory.SignInAsync(DemoData.AdminB);
        var org = await adminB.CreateWorkspaceAsync();

        await (await adminB.PostAsJsonAsync($"/api/v1/organizations/{org.Id}/ai-generations", GenerationBody("none")))
            .AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.AiNotConfigured);
    }

    [Fact]
    public async Task Api_key_is_stored_encrypted_and_never_returned()
    {
        var admin = await EnsureAiConfiguredAsync();
        var creator = await factory.SignInAsync(DemoData.CreatorA);

        var raw = await (await admin.GetAsync($"{OrgA}/ai-settings")).Content.ReadAsStringAsync();
        var models = await (await admin.PostAsJsonAsync($"{OrgA}/ai-settings/models", new { Provider = "OpenAI" })).ReadAsync<List<string>>();
        var creatorSave = await creator.PutJsonAsync($"{OrgA}/ai-settings", new { Provider = "Groq", Model = "m", ApiKey = "x" }, ifMatch: "\"AAAA\"");
        var changeEndpointWithoutKey = await admin.PutJsonAsync($"{OrgA}/ai-settings", new { Provider = "OpenAICompatible", BaseUrl = "https://llm.attacker.example/v1", Model = "m" },
            ApiClient.ETag((await (await admin.GetAsync($"{OrgA}/ai-settings")).ReadAsync<AiSettingsDto>()).RowVersion));

        Assert.DoesNotContain(ApiKey, raw);
        Assert.Contains("…WXYZ", raw);
        Assert.Equal(["gpt-test", "gpt-test-mini"], models);
        Assert.Equal(ApiKey, factory.Llm.Calls[^1].Connection.ApiKey); // the stored key was decrypted server-side for its own endpoint
        await creatorSave.AssertErrorAsync(HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        await changeEndpointWithoutKey.AssertErrorAsync(HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        await using var db = factory.CreateDbContext();
        var stored = await db.AiProviderSettings.AsNoTracking().SingleAsync(s => s.OrganizationId == DemoData.OrganizationAId);
        Assert.DoesNotContain(ApiKey, stored.ProtectedApiKey);
    }
}
