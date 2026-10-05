using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Infrastructure.OAuth;

namespace SocialMediaAutomation.IntegrationTests;

/// <summary>The provider OAuth clients against a stub HTTP endpoint (no real provider is called).</summary>
public class OAuthGatewayTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request, request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            return respond(request);
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static SocialOAuthGateway Gateway(StubHandler handler, bool configured = true) => new(ProviderAppSource.FromConfiguration(new ConfigurationBuilder()
        .AddInMemoryCollection(configured
            ? new Dictionary<string, string?>
            {
                ["SocialProviders:LinkedIn:ClientId"] = "li-client", ["SocialProviders:LinkedIn:ClientSecret"] = "li-secret-value",
                ["SocialProviders:LinkedIn:RedirectUri"] = "http://localhost:4200/api/v1/oauth/linkedin/callback",
                ["SocialProviders:Instagram:AppId"] = "meta-app", ["SocialProviders:Instagram:AppSecret"] = "meta-secret-value",
                ["SocialProviders:Instagram:RedirectUri"] = "http://localhost:4200/api/v1/oauth/instagram/callback",
                ["SocialProviders:Instagram:GraphApiVersion"] = "v21.0",
            }
            : new Dictionary<string, string?> { ["SocialProviders:LinkedIn:ClientId"] = "li-client" })
        .Build()), new Factory(handler));

    [Fact]
    public void Provider_is_unavailable_without_a_client_secret()
    {
        var gateway = Gateway(new StubHandler(_ => Json("{}")), configured: false);
        Assert.False(gateway.IsConfigured(SocialPlatform.LinkedIn));
        Assert.Throws<OAuthProviderException>(() => gateway.BuildAuthorizationUrl(SocialPlatform.LinkedIn, "s"));
    }

    [Fact]
    public async Task LinkedIn_exchanges_the_code_server_side_with_the_secret_only_in_the_body()
    {
        var handler = new StubHandler(r => r.RequestUri!.AbsolutePath.EndsWith("accessToken")
            ? Json("""{"access_token":"li-token","expires_in":5184000,"scope":"openid,profile,w_member_social"}""")
            : Json("""{"sub":"abc123","name":"Ada Lovelace"}"""));
        var gateway = Gateway(handler);

        var url = gateway.BuildAuthorizationUrl(SocialPlatform.LinkedIn, "state-value");
        Assert.StartsWith(LinkedInOAuthClient.AuthorizeEndpoint, url);
        Assert.Contains("state=state-value", url);
        Assert.DoesNotContain("li-secret-value", url);

        var grant = await gateway.ExchangeCodeAsync(SocialPlatform.LinkedIn, "the-code", CancellationToken.None);
        var accounts = await gateway.DiscoverAccountsAsync(SocialPlatform.LinkedIn, grant, CancellationToken.None);

        var (tokenRequest, tokenBody) = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, tokenRequest.Method);
        Assert.Contains("client_secret=li-secret-value", tokenBody);
        Assert.Contains("code=the-code", tokenBody);
        Assert.DoesNotContain("secret", tokenRequest.RequestUri!.ToString());
        Assert.Equal(("Bearer", "li-token"), (handler.Requests[1].Request.Headers.Authorization!.Scheme, handler.Requests[1].Request.Headers.Authorization!.Parameter));
        Assert.Equal(("abc123", "Ada Lovelace"), (grant.ExternalUserId, grant.DisplayName));
        Assert.DoesNotContain("li-token", grant.ToString());
        Assert.Equal("urn:li:person:abc123", Assert.Single(accounts).ExternalAccountId);
    }

    [Fact]
    public async Task Instagram_discovers_professional_accounts_linked_to_pages()
    {
        var handler = new StubHandler(r => r.RequestUri!.AbsolutePath switch
        {
            var p when p.EndsWith("/oauth/access_token") => Json("""{"access_token":"meta-token","expires_in":3600}"""),
            var p when p.EndsWith("/me/permissions") => Json("""{"data":[{"permission":"instagram_content_publish","status":"granted"},{"permission":"instagram_basic","status":"declined"}]}"""),
            var p when p.EndsWith("/me/accounts") => Json("""{"data":[{"id":"p1","name":"Brightline","instagram_business_account":{"id":"ig1","username":"brightline"}},{"id":"p2","name":"No IG"}]}"""),
            _ => Json("""{"id":"fb-user","name":"Ada"}"""),
        });
        var gateway = Gateway(handler);

        var grant = await gateway.ExchangeCodeAsync(SocialPlatform.Instagram, "c", CancellationToken.None);
        var account = Assert.Single(await gateway.DiscoverAccountsAsync(SocialPlatform.Instagram, grant, CancellationToken.None));

        Assert.Equal(["instagram_content_publish"], grant.Scopes);
        Assert.Equal(("ig1", "@brightline (via Brightline)"), (account.ExternalAccountId, account.DisplayName));
        Assert.Equal(["publish_image"], account.Capabilities);
        Assert.All(handler.Requests.Skip(1), r => Assert.DoesNotContain("meta-token", r.Request.RequestUri!.ToString()));
    }

    [Fact]
    public async Task Provider_errors_become_safe_oauth_failures()
    {
        var gateway = Gateway(new StubHandler(_ => Json("""{"error":"invalid_request","error_description":"code expired"}""", HttpStatusCode.BadRequest)));
        var error = await Assert.ThrowsAsync<OAuthProviderException>(() => gateway.ExchangeCodeAsync(SocialPlatform.LinkedIn, "c", CancellationToken.None));
        Assert.Contains("code expired", error.Message);
        Assert.DoesNotContain("li-secret-value", error.Message);
    }
}
