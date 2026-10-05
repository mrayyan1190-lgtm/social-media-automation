using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using SocialMediaAutomation.Application.Campaigns.DTOs;
using SocialMediaAutomation.Application.Posts.DTOs;
using SocialMediaAutomation.Application.SocialAccounts.DTOs;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Infrastructure.Persistence.Seed;
using SocialMediaAutomation.IntegrationTests.Infrastructure;

namespace SocialMediaAutomation.IntegrationTests;

/// <summary>OAuth connection management and destination modes through the real API, with a fake provider gateway.</summary>
[Collection(ApiCollection.Name)]
public class SocialConnectionTests(ApiFactory factory)
{
    private static readonly string OrgA = $"/api/v1/organizations/{DemoData.OrganizationAId}";
    private static readonly string OrgB = $"/api/v1/organizations/{DemoData.OrganizationBId}";

    private static string Code() => $"u{Guid.NewGuid():N}";

    private static async Task<string> StartAsync(HttpClient admin, string platform, ExecutionMode mode = ExecutionMode.LIVE)
    {
        var response = await (await admin.PostAsJsonAsync($"{OrgA}/social-connections/{platform}/start", new { Mode = mode.ToString() }))
            .ReadAsync<StartResponse>();
        return Uri.UnescapeDataString(new Uri(response.AuthorizationUrl).Query.Split("state=")[1]);
    }

    private sealed record StartResponse(string AuthorizationUrl);

    /// <summary>The provider redirect arrives without the session cookie; returns the web redirect target.</summary>
    private async Task<Uri> CallbackAsync(string platform, string? state, string? code, string? error = null)
    {
        var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await anonymous.GetAsync(
            $"/api/v1/oauth/{platform.ToLowerInvariant()}/callback?state={Uri.EscapeDataString(state ?? "")}&code={code}&error={error}");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.StartsWith("http://web.test/social-accounts?", location.ToString());
        return location;
    }

    private static bool Succeeded(Uri location) => location.Query.Contains("status=connected");

    private async Task<SocialAccountDto?> AccountForAsync(HttpClient client, string code) =>
        (await (await client.GetAsync($"{OrgA}/social-accounts")).ReadAsync<List<SocialAccountDto>>())
        .SingleOrDefault(a => a.ExternalAccountId == $"acct-{code}");

    [Fact]
    public async Task Providers_show_live_availability_and_an_honest_blocked_reason()
    {
        var creator = await factory.SignInAsync(DemoData.CreatorA);
        var providers = await (await creator.GetAsync($"{OrgA}/social-connections/providers")).ReadAsync<List<SocialProviderDto>>();

        Assert.True(providers.Single(p => p.Platform == SocialPlatform.LinkedIn).LiveConnectionAvailable);
        var instagram = providers.Single(p => p.Platform == SocialPlatform.Instagram);
        Assert.False(instagram.LiveConnectionAvailable);
        Assert.Contains("blocked", instagram.BlockedReason);

        var admin = await factory.SignInAsync(DemoData.AdminA);
        await (await admin.PostAsJsonAsync($"{OrgA}/social-connections/Instagram/start", new { Mode = "LIVE" }))
            .AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.ProviderNotConfigured);
    }

    [Fact]
    public async Task Only_admins_start_connections_and_only_for_live_or_test_modes()
    {
        foreach (var user in new[] { DemoData.CreatorA, DemoData.ReviewerA })
        {
            var client = await factory.SignInAsync(user);
            await (await client.PostAsJsonAsync($"{OrgA}/social-connections/LinkedIn/start", new { Mode = "LIVE" }))
                .AssertErrorAsync(HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        }

        var admin = await factory.SignInAsync(DemoData.AdminA);
        await (await admin.PostAsJsonAsync($"{OrgA}/social-connections/LinkedIn/start", new { Mode = "SIMULATED" }))
            .AssertErrorAsync(HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task Callback_creates_a_live_destination_and_never_exposes_or_stores_raw_tokens()
    {
        var admin = await factory.SignInAsync(DemoData.AdminA);
        var code = Code();
        var location = await CallbackAsync("LinkedIn", await StartAsync(admin, "LinkedIn"), code);
        Assert.True(Succeeded(location), location.ToString());

        var account = await AccountForAsync(admin, code);
        Assert.NotNull(account);
        Assert.Equal((ExecutionMode.LIVE, ConnectionStatus.CONNECTED, true), (account.ExecutionMode, account.ConnectionStatus, account.IsEligibleDestination));

        // Nothing the browser can read contains token material.
        var raw = string.Concat(
            await admin.GetStringAsync($"{OrgA}/social-accounts"),
            await admin.GetStringAsync($"{OrgA}/social-connections"),
            await admin.GetStringAsync($"{OrgA}/audit"),
            location.ToString());
        Assert.DoesNotContain(FakeOAuthGateway.AccessToken(code), raw);
        Assert.DoesNotContain(FakeOAuthGateway.RefreshToken(code), raw);
        Assert.DoesNotContain("ProtectedAccessToken", raw, StringComparison.OrdinalIgnoreCase);

        // Stored only in protected form.
        await using var db = factory.CreateDbContext();
        var connection = await db.OAuthConnections.SingleAsync(c => c.ExternalUserId == code);
        Assert.Equal((DemoData.OrganizationAId, OAuthConnectionStatus.ACTIVE), (connection.OrganizationId, connection.Status));
        Assert.DoesNotContain(FakeOAuthGateway.AccessToken(code), connection.ProtectedAccessToken);
        Assert.DoesNotContain(FakeOAuthGateway.RefreshToken(code), connection.ProtectedRefreshToken!);
    }

    [Fact]
    public async Task Invalid_replayed_expired_or_mismatched_state_is_rejected_without_creating_a_connection()
    {
        var admin = await factory.SignInAsync(DemoData.AdminA);

        var forged = Code();
        Assert.False(Succeeded(await CallbackAsync("LinkedIn", "not-a-real-state", forged)));
        Assert.False(Succeeded(await CallbackAsync("LinkedIn", null, forged)));

        var replayed = Code();
        var state = await StartAsync(admin, "LinkedIn");
        Assert.True(Succeeded(await CallbackAsync("LinkedIn", state, replayed)));
        var replay = await CallbackAsync("LinkedIn", state, forged);
        Assert.False(Succeeded(replay));
        Assert.Contains("already", Uri.UnescapeDataString(replay.Query));

        var mismatched = Code();
        Assert.False(Succeeded(await CallbackAsync("Facebook", await StartAsync(admin, "LinkedIn"), mismatched)));

        var expired = Code();
        var expiredState = await StartAsync(admin, "Facebook");
        await using (var db = factory.CreateDbContext())
            await db.OAuthStates.Where(s => s.StateHash == OAuthState.Hash(expiredState))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAtUtc, DateTime.UtcNow.AddMinutes(-1)));
        Assert.False(Succeeded(await CallbackAsync("Facebook", expiredState, expired)));

        var denied = Code();
        Assert.False(Succeeded(await CallbackAsync("Facebook", await StartAsync(admin, "Facebook"), denied, error: "access_denied")));
        Assert.False(Succeeded(await CallbackAsync("Facebook", await StartAsync(admin, "Facebook"), "fail")));

        await using var check = factory.CreateDbContext();
        var codes = new[] { forged, mismatched, expired, denied, "fail" };
        Assert.False(await check.OAuthConnections.AnyAsync(c => codes.Contains(c.ExternalUserId)));
        Assert.True(await check.OAuthConnections.AnyAsync(c => c.ExternalUserId == replayed));
    }

    [Fact]
    public async Task Connections_are_tenant_isolated()
    {
        var adminA = await factory.SignInAsync(DemoData.AdminA);
        var code = Code();
        await CallbackAsync("LinkedIn", await StartAsync(adminA, "LinkedIn"), code);
        var connection = (await (await adminA.GetAsync($"{OrgA}/social-connections")).ReadAsync<List<SocialConnectionDto>>())
            .Single(c => c.DisplayName == $"User {code}");

        var adminB = await factory.SignInAsync(DemoData.AdminB);
        await (await adminB.GetAsync($"{OrgA}/social-connections")).AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
        await (await adminB.GetAsync($"{OrgA}/social-accounts")).AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
        await (await adminB.PostAsync($"{OrgB}/social-connections/{connection.Id}/disconnect", null))
            .AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
        Assert.DoesNotContain(await adminB.GetStringAsync($"{OrgB}/social-accounts"), $"acct-{code}");
        Assert.True((await AccountForAsync(adminA, code))!.IsEligibleDestination);
    }

    [Fact]
    public async Task Disconnect_revokes_tokens_and_blocks_submission_without_switching_to_simulated()
    {
        var admin = await factory.SignInAsync(DemoData.AdminA);
        var code = Code();
        await CallbackAsync("LinkedIn", await StartAsync(admin, "LinkedIn", ExecutionMode.TEST_ACCOUNT), code);
        var account = (await AccountForAsync(admin, code))!;
        var connection = (await (await admin.GetAsync($"{OrgA}/social-connections")).ReadAsync<List<SocialConnectionDto>>())
            .Single(c => c.DisplayName == $"User {code}");

        var creator = await factory.SignInAsync(DemoData.CreatorA);
        await (await creator.PostAsync($"{OrgA}/social-connections/{connection.Id}/disconnect", null))
            .AssertErrorAsync(HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"{OrgA}/social-connections/{connection.Id}/disconnect", null)).StatusCode);

        var after = (await AccountForAsync(admin, code))!;
        Assert.Equal((ExecutionMode.TEST_ACCOUNT, ConnectionStatus.DISCONNECTED, false), (after.ExecutionMode, after.ConnectionStatus, after.IsEligibleDestination));
        await using (var db = factory.CreateDbContext())
            Assert.Equal("", (await db.OAuthConnections.SingleAsync(c => c.Id == connection.Id)).ProtectedAccessToken);

        // Content aimed at the disconnected destination cannot enter review.
        var campaign = await creator.CreateCampaignAsync(OrgA, "LinkedIn");
        var post = await (await creator.PostAsJsonAsync($"{OrgA}/campaigns/{campaign.Id}/posts",
            new { Platform = "LinkedIn", Hook = "Month-end, minus the pain", Body = "Close the books in days.", SocialAccountId = account.Id })).ReadAsync<PostDto>();
        var submit = await creator.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"{OrgA}/posts/{post.Id}/submit-review")
        {
            Content = JsonContent.Create(new { }),
            Headers = { { "If-Match", ApiClient.ETag(post.RowVersion) } },
        });
        await submit.AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.SubmissionBlocked);
    }

    [Fact]
    public async Task Reconnect_refreshes_the_destination_but_never_changes_its_mode()
    {
        var admin = await factory.SignInAsync(DemoData.AdminA);
        var code = Code();
        await CallbackAsync("Facebook", await StartAsync(admin, "Facebook", ExecutionMode.LIVE), code);
        var first = (await AccountForAsync(admin, code))!;

        var location = await CallbackAsync("Facebook", await StartAsync(admin, "Facebook", ExecutionMode.TEST_ACCOUNT), code);
        Assert.Contains("kept their previous execution mode", Uri.UnescapeDataString(location.Query));
        var again = (await AccountForAsync(admin, code))!;
        Assert.Equal((first.Id, ExecutionMode.LIVE, true), (again.Id, again.ExecutionMode, again.IsEligibleDestination));
    }

    [Fact]
    public async Task Simulated_destinations_are_explicitly_labelled_and_never_live()
    {
        var reviewer = await factory.SignInAsync(DemoData.ReviewerA);
        var json = await reviewer.GetStringAsync($"{OrgA}/social-accounts");
        Assert.Contains("\"executionMode\":\"SIMULATED\"", json);
        Assert.Contains("\"executionMode\":\"NOT_CONFIGURED\"", json);

        var accounts = await (await reviewer.GetAsync($"{OrgA}/social-accounts")).ReadAsync<List<SocialAccountDto>>();
        Assert.All(accounts.Where(a => a.ExecutionMode == ExecutionMode.SIMULATED), a => Assert.Null(a.ExternalAccountId));
        Assert.All(accounts.Where(a => a.ExecutionMode == ExecutionMode.NOT_CONFIGURED), a => Assert.False(a.IsEligibleDestination));
    }
}
