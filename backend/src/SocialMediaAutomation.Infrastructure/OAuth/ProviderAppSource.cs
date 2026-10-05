using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.Platform;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Infrastructure.OAuth;

/// <summary>
/// Provider app credentials: the platform settings saved in /internal/social-providers (secret from the secret store)
/// when present, otherwise the deployment configuration ("SocialProviders:{Platform}"). Held as an in-memory snapshot so
/// the OAuth gateway stays synchronous; refreshed at startup, after every save and in the background every minute.
/// </summary>
internal sealed class ProviderAppSource(IConfiguration configuration, IServiceScopeFactory? scopes, ILogger<ProviderAppSource>? logger) : IProviderAppCache
{
    /// <summary>Configuration only, never reading platform settings (tests and tools without a database).</summary>
    public static ProviderAppSource FromConfiguration(IConfiguration configuration) => new(configuration, null, null);

    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(1);
    private IReadOnlyDictionary<PlatformProvider, (SocialProviderConfiguration Config, string? Secret)> snapshot =
        new Dictionary<PlatformProvider, (SocialProviderConfiguration, string?)>();
    private DateTime loadedAtUtc = DateTime.MinValue;
    private int refreshing;

    public ProviderApp Get(SocialPlatform platform)
    {
        if (scopes is not null && DateTime.UtcNow - loadedAtUtc > MaxAge && Interlocked.Exchange(ref refreshing, 1) == 0)
            _ = Task.Run(async () =>
            {
                try { await RefreshAsync(CancellationToken.None); }
                finally { Interlocked.Exchange(ref refreshing, 0); }
            });

        var provider = platform == SocialPlatform.LinkedIn ? PlatformProvider.LINKEDIN : PlatformProvider.META;
        var fallback = ProviderApp.From(configuration, platform);
        if (!snapshot.TryGetValue(provider, out var stored))
            return fallback;
        var config = stored.Config;
        // A disabled provider is not configured, even if the deployment configuration has credentials.
        if (!config.Enabled)
            return fallback with { ClientId = "", ClientSecret = "", RedirectUri = "" };
        var redirect = platform == SocialPlatform.Instagram
            ? SocialProviderConfigurationService.InstagramRedirect(config.RedirectUri) ?? config.RedirectUri
            : config.RedirectUri;
        return new ProviderApp(config.ClientId, stored.Secret ?? "", redirect, config.ApiVersion ?? fallback.ApiVersion);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (scopes is null)
            return;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var platform = scope.ServiceProvider.GetRequiredService<IPlatformRepository>();
            var secrets = scope.ServiceProvider.GetRequiredService<ISecretStore>();
            var next = new Dictionary<PlatformProvider, (SocialProviderConfiguration, string?)>();
            foreach (var config in await platform.ListProvidersAsync(cancellationToken))
                next[config.Provider] = (config, config.HasSecret ? await secrets.GetAsync(SocialProviderConfiguration.SecretName(config.Provider), cancellationToken) : null);
            snapshot = next;
            loadedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Keep the previous snapshot (or the configuration fallback); try again on the next read.
            loadedAtUtc = DateTime.UtcNow;
            logger?.LogWarning(ex, "Could not load social provider settings");
        }
    }
}

/// <summary>Meta offers an app access token for valid app credentials; LinkedIn has no equivalent check.</summary>
internal sealed class ProviderAppValidator(IHttpClientFactory httpClientFactory) : IProviderAppValidator
{
    public async Task<(bool Valid, string Message)?> ValidateAsync(PlatformProvider provider, string clientId, string clientSecret, CancellationToken cancellationToken)
    {
        if (provider != PlatformProvider.META)
            return null;
        var http = httpClientFactory.CreateClient(SocialOAuthGateway.HttpClientName);
        try
        {
            var url = $"https://graph.facebook.com/oauth/access_token?client_id={Uri.EscapeDataString(clientId)}" +
                $"&client_secret={Uri.EscapeDataString(clientSecret)}&grant_type=client_credentials";
            using var response = await http.GetAsync(url, cancellationToken);
            return response.IsSuccessStatusCode
                ? (true, "Meta accepted the App ID and App Secret.")
                : (false, response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized
                    ? "Meta rejected the App ID or App Secret."
                    : $"Meta answered {(int)response.StatusCode}; try again later.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return (false, "Meta could not be reached.");
        }
    }
}
