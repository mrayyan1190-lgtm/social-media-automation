using System.Text.Json.Nodes;
using SocialMediaAutomation.Application.Abstractions;

namespace SocialMediaAutomation.Infrastructure.OAuth;

/// <summary>
/// Facebook Login for Facebook Pages and Instagram professional accounts (Graph API). Facebook discovers the Pages the
/// user can create content on; Instagram discovers the professional accounts linked to those Pages.
/// ponytail: keeps the short-lived user token; exchange for a long-lived token when real publishing lands (TASKS.md Phase 18-19).
/// </summary>
internal sealed class MetaOAuthClient(ProviderApp app, HttpClient http, bool instagram) : IProviderOAuthClient
{
    public static readonly string[] FacebookScopes = ["pages_show_list", "pages_read_engagement", "pages_manage_posts"];
    public static readonly string[] InstagramScopes = ["pages_show_list", "instagram_basic", "instagram_content_publish", "business_management"];

    private string Version => string.IsNullOrWhiteSpace(app.ApiVersion) ? "v21.0" : app.ApiVersion;
    private string Graph => $"https://graph.facebook.com/{Version}";
    private string Provider => instagram ? "Instagram" : "Facebook";

    public string BuildAuthorizationUrl(string state) =>
        $"https://www.facebook.com/{Version}/dialog/oauth?response_type=code&client_id={Uri.EscapeDataString(app.ClientId)}" +
        $"&redirect_uri={Uri.EscapeDataString(app.RedirectUri)}&state={Uri.EscapeDataString(state)}" +
        // Facebook Login for Business: the configuration (set up in the Meta app) defines the permissions instead of scope.
        (string.IsNullOrWhiteSpace(app.ConfigurationId)
            ? $"&scope={Uri.EscapeDataString(string.Join(',', instagram ? InstagramScopes : FacebookScopes))}"
            : $"&config_id={Uri.EscapeDataString(app.ConfigurationId)}");

    public async Task<OAuthGrant> ExchangeCodeAsync(string code, CancellationToken cancellationToken)
    {
        var token = await OAuthHttp.SendAsync(http, OAuthHttp.PostForm($"{Graph}/oauth/access_token",
        [
            new("client_id", app.ClientId), new("client_secret", app.ClientSecret), new("redirect_uri", app.RedirectUri), new("code", code),
        ]), $"{Provider} token exchange", cancellationToken);
        var accessToken = token["access_token"]?.GetValue<string>() ?? throw new OAuthProviderException($"{Provider} returned no access token.");

        var me = await OAuthHttp.SendAsync(http, OAuthHttp.Get($"{Graph}/me?fields=id,name", accessToken), $"{Provider} profile lookup", cancellationToken);
        var permissions = await OAuthHttp.SendAsync(http, OAuthHttp.Get($"{Graph}/me/permissions", accessToken), $"{Provider} permission lookup", cancellationToken);
        var granted = (permissions["data"]?.AsArray() ?? [])
            .Where(p => p?["status"]?.GetValue<string>() == "granted")
            .Select(p => p!["permission"]!.GetValue<string>())
            .ToList();

        return new OAuthGrant(accessToken, null, OAuthHttp.ExpiresAt(token), granted,
            me["id"]?.GetValue<string>() ?? throw new OAuthProviderException($"{Provider} returned no user id."),
            me["name"]?.GetValue<string>() ?? $"{Provider} user");
    }

    /// <summary>debug_token (with the app token) proves the token is a valid Page token of this app; /me names the Page.</summary>
    public async Task<OAuthGrant> InspectPageTokenAsync(string pageToken, CancellationToken cancellationToken)
    {
        var debug = (await OAuthHttp.SendAsync(http, OAuthHttp.Get($"{Graph}/debug_token?input_token={Uri.EscapeDataString(pageToken)}",
            $"{app.ClientId}|{app.ClientSecret}"), "Facebook token check", cancellationToken))["data"];
        if (debug?["is_valid"]?.GetValue<bool>() != true)
            throw new OAuthProviderException("the token is not valid (expired or revoked).");
        if (debug["app_id"]?.GetValue<string>() != app.ClientId)
            throw new OAuthProviderException("the token was issued to a different Meta app than the one configured.");
        if (!string.Equals(debug["type"]?.GetValue<string>(), "PAGE", StringComparison.OrdinalIgnoreCase))
            throw new OAuthProviderException("this is not a Page access token.");
        var scopes = (debug["scopes"]?.AsArray() ?? []).Select(s => s!.GetValue<string>()).ToList();
        DateTime? expires = debug["expires_at"]?.GetValue<long>() is long at and > 0 ? DateTimeOffset.FromUnixTimeSeconds(at).UtcDateTime : null;

        var page = await OAuthHttp.SendAsync(http, OAuthHttp.Get($"{Graph}/me?fields=id,name", pageToken), "Facebook Page lookup", cancellationToken);
        return new OAuthGrant(pageToken, null, expires, scopes,
            page["id"]?.GetValue<string>() ?? throw new OAuthProviderException("Facebook returned no Page id."),
            page["name"]?.GetValue<string>() ?? "Facebook Page");
    }

    public async Task<IReadOnlyList<DiscoveredAccount>> DiscoverAccountsAsync(OAuthGrant grant, CancellationToken cancellationToken)
    {
        var fields = instagram ? "id,name,instagram_business_account{id,username}" : "id,name,tasks";
        var pages = await OAuthHttp.SendAsync(http, OAuthHttp.Get($"{Graph}/me/accounts?fields={Uri.EscapeDataString(fields)}", grant.AccessToken),
            $"{Provider} account discovery", cancellationToken);
        var data = pages["data"]?.AsArray() ?? [];
        return instagram ? InstagramAccounts(data, grant) : FacebookPages(data, grant);
    }

    private static List<DiscoveredAccount> FacebookPages(JsonArray pages, OAuthGrant grant)
    {
        var canPost = grant.Scopes.Contains("pages_manage_posts");
        return pages
            .Where(p => p?["id"] is not null)
            .Select(p =>
            {
                var tasks = p!["tasks"]?.AsArray().Select(t => t?.GetValue<string>()).ToList() ?? [];
                var capabilities = canPost && tasks.Contains("CREATE_CONTENT") ? new List<string> { "publish_text", "publish_link", "publish_image" } : [];
                return new DiscoveredAccount(p["id"]!.GetValue<string>(), $"{p["name"]?.GetValue<string>() ?? "Facebook Page"} (Facebook Page)", capabilities);
            })
            .ToList();
    }

    private static List<DiscoveredAccount> InstagramAccounts(JsonArray pages, OAuthGrant grant)
    {
        var capabilities = grant.Scopes.Contains("instagram_content_publish") ? new List<string> { "publish_image" } : [];
        return pages
            .Select(p => (Page: p, Ig: p?["instagram_business_account"]))
            .Where(x => x.Ig?["id"] is not null)
            .Select(x => new DiscoveredAccount(x.Ig!["id"]!.GetValue<string>(),
                $"@{x.Ig["username"]?.GetValue<string>() ?? "instagram"} (via {x.Page?["name"]?.GetValue<string>() ?? "Page"})", capabilities))
            .ToList();
    }
}
