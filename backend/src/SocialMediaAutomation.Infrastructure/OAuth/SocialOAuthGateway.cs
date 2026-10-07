using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Infrastructure.OAuth;

/// <summary>App credentials for one provider ("SocialProviders:{Platform}"). Secrets come from user-secrets or environment only.</summary>
public sealed record ProviderApp(string ClientId, string ClientSecret, string RedirectUri, string ApiVersion, string? ConfigurationId = null)
{
    public bool IsComplete => ClientId.Length > 0 && ClientSecret.Length > 0 && RedirectUri.Length > 0;

    public static ProviderApp From(IConfiguration configuration, SocialPlatform platform)
    {
        var section = configuration.GetSection($"SocialProviders:{platform}");
        return new ProviderApp(
            section["ClientId"] ?? section["AppId"] ?? "",
            section["ClientSecret"] ?? section["AppSecret"] ?? "",
            section["RedirectUri"] ?? "",
            section["ApiVersion"] ?? section["GraphApiVersion"] ?? "",
            section["ConfigurationId"]);
    }
}

/// <summary>Routes each OAuth step to the provider's own client; no provider conditionals leak outside this folder.</summary>
internal sealed class SocialOAuthGateway(ProviderAppSource apps, IHttpClientFactory httpClientFactory) : ISocialOAuthGateway
{
    public const string HttpClientName = "social-oauth";

    public bool IsConfigured(SocialPlatform platform) => apps.Get(platform).IsComplete;

    public string BuildAuthorizationUrl(SocialPlatform platform, string state) => Client(platform).BuildAuthorizationUrl(state);

    public Task<OAuthGrant> ExchangeCodeAsync(SocialPlatform platform, string code, CancellationToken cancellationToken) =>
        Client(platform).ExchangeCodeAsync(code, cancellationToken);

    public Task<IReadOnlyList<DiscoveredAccount>> DiscoverAccountsAsync(SocialPlatform platform, OAuthGrant grant, CancellationToken cancellationToken) =>
        Client(platform).DiscoverAccountsAsync(grant, cancellationToken);

    private IProviderOAuthClient Client(SocialPlatform platform)
    {
        var app = apps.Get(platform);
        if (!app.IsComplete)
            throw new OAuthProviderException($"{platform} app credentials are not configured.");
        var http = httpClientFactory.CreateClient(HttpClientName);
        return platform switch
        {
            SocialPlatform.LinkedIn => new LinkedInOAuthClient(app, http),
            SocialPlatform.Facebook => new MetaOAuthClient(app, http, instagram: false),
            SocialPlatform.Instagram => new MetaOAuthClient(app, http, instagram: true),
            _ => throw new OAuthProviderException($"{platform} is not supported."),
        };
    }
}

internal interface IProviderOAuthClient
{
    string BuildAuthorizationUrl(string state);

    Task<OAuthGrant> ExchangeCodeAsync(string code, CancellationToken cancellationToken);

    Task<IReadOnlyList<DiscoveredAccount>> DiscoverAccountsAsync(OAuthGrant grant, CancellationToken cancellationToken);
}

/// <summary>Shared HTTP helpers: provider errors become <see cref="OAuthProviderException"/> without echoing secrets.</summary>
internal static class OAuthHttp
{
    public static async Task<JsonNode> SendAsync(HttpClient http, HttpRequestMessage request, string step, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new OAuthProviderException($"the provider could not be reached during {step}.");
        }
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            JsonNode? json = null;
            try
            {
                json = JsonNode.Parse(body);
            }
            catch (System.Text.Json.JsonException)
            {
                // fall through: reported below
            }
            if (!response.IsSuccessStatusCode || json is null)
            {
                var message = json?["error_description"]?.GetValue<string>() ?? json?["error"]?["message"]?.GetValue<string>()
                    ?? json?["message"]?.GetValue<string>() ?? $"HTTP {(int)response.StatusCode}";
                throw new OAuthProviderException($"{step} failed: {message}");
            }
            return json;
        }
    }

    public static HttpRequestMessage Get(string url, string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    /// <summary>Client secret and code travel in a form body, never in the URL (URLs end up in HTTP logs).</summary>
    public static HttpRequestMessage PostForm(string url, IEnumerable<KeyValuePair<string, string>> fields) =>
        new(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(fields) };

    public static DateTime? ExpiresAt(JsonNode json) =>
        json["expires_in"]?.GetValue<long>() is long seconds and > 0 ? DateTime.UtcNow.AddSeconds(seconds) : null;
}
