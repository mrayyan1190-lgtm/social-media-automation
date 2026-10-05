using SocialMediaAutomation.Application.Abstractions;

namespace SocialMediaAutomation.Infrastructure.OAuth;

/// <summary>
/// LinkedIn OAuth 2.0 (3-legged) with OpenID Connect sign-in and member posting ("Share on LinkedIn" product).
/// Discovers the authorized member as the destination. Organization pages need LinkedIn's Community Management API
/// approval and are not discovered in the MVP.
/// </summary>
internal sealed class LinkedInOAuthClient(ProviderApp app, HttpClient http) : IProviderOAuthClient
{
    public const string AuthorizeEndpoint = "https://www.linkedin.com/oauth/v2/authorization";
    public const string TokenEndpoint = "https://www.linkedin.com/oauth/v2/accessToken";
    public const string UserInfoEndpoint = "https://api.linkedin.com/v2/userinfo";
    public static readonly string[] RequestedScopes = ["openid", "profile", "w_member_social"];

    public string BuildAuthorizationUrl(string state) =>
        $"{AuthorizeEndpoint}?response_type=code&client_id={Uri.EscapeDataString(app.ClientId)}" +
        $"&redirect_uri={Uri.EscapeDataString(app.RedirectUri)}&state={Uri.EscapeDataString(state)}" +
        $"&scope={Uri.EscapeDataString(string.Join(' ', RequestedScopes))}";

    public async Task<OAuthGrant> ExchangeCodeAsync(string code, CancellationToken cancellationToken)
    {
        var token = await OAuthHttp.SendAsync(http, OAuthHttp.PostForm(TokenEndpoint,
        [
            new("grant_type", "authorization_code"), new("code", code), new("redirect_uri", app.RedirectUri),
            new("client_id", app.ClientId), new("client_secret", app.ClientSecret),
        ]), "LinkedIn token exchange", cancellationToken);
        var accessToken = token["access_token"]?.GetValue<string>() ?? throw new OAuthProviderException("LinkedIn returned no access token.");

        var me = await OAuthHttp.SendAsync(http, OAuthHttp.Get(UserInfoEndpoint, accessToken), "LinkedIn profile lookup", cancellationToken);
        var memberId = me["sub"]?.GetValue<string>() ?? throw new OAuthProviderException("LinkedIn returned no member id.");
        var scopes = (token["scope"]?.GetValue<string>() ?? "").Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);

        return new OAuthGrant(accessToken, token["refresh_token"]?.GetValue<string>(), OAuthHttp.ExpiresAt(token), scopes,
            memberId, me["name"]?.GetValue<string>() ?? "LinkedIn member");
    }

    public Task<IReadOnlyList<DiscoveredAccount>> DiscoverAccountsAsync(OAuthGrant grant, CancellationToken cancellationToken)
    {
        IReadOnlyList<DiscoveredAccount> accounts = grant.Scopes.Contains("w_member_social")
            ? [new DiscoveredAccount($"urn:li:person:{grant.ExternalUserId}", $"{grant.DisplayName} (LinkedIn member)", ["publish_text", "publish_image"])]
            : [];
        return Task.FromResult(accounts);
    }
}
