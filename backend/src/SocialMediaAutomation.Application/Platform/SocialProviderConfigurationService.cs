using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.Common;
using SocialMediaAutomation.Application.Common.Authorization;
using SocialMediaAutomation.Domain.Common;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Application.Platform;

/// <summary>
/// One platform integration as /internal/social-providers shows it. The client secret is never returned: only whether
/// one is configured. Source: PLATFORM (set here), ENVIRONMENT (deployment configuration) or NONE.
/// Status: CONFIGURED, INCOMPLETE, DISABLED or NOT_CONFIGURED.
/// </summary>
public sealed record SocialProviderDto(
    PlatformProvider Provider, string Source, string ClientId, string RedirectUri, string? DerivedRedirectUri, string? ApiVersion, string? ConfigurationId, bool Enabled,
    bool SecretConfigured, DateTime? SecretUpdatedAtUtc, DateTime? UpdatedAtUtc, string? RowVersion, string ConfigurationStatus,
    DateTime? LastSuccessAtUtc, DateTime? LastFailureAtUtc, string? LastFailure, int FailuresLast24Hours);

public sealed record SaveSocialProviderCommand(PlatformProvider Provider, string ClientId, string? RedirectUri, string? ApiVersion, string? ConfigurationId, bool Enabled, byte[]? ExpectedRowVersion);

public sealed record ProviderValidationDto(bool Valid, string Message);

/// <summary>"Continue with Google". Source: PLATFORM (saved here), ENVIRONMENT (deployment configuration) or NONE. The secret is never returned.</summary>
public sealed record GoogleSignInDto(string Source, string ClientId, bool SecretConfigured);

/// <summary>
/// The operator's provider apps (LinkedIn; Meta for Facebook and Instagram). Platform settings override the deployment
/// configuration; secrets go to the secret store and are never displayed again.
/// </summary>
public sealed class SocialProviderConfigurationService(
    SystemAdminAuthorizer systemAdmins,
    IPlatformRepository platform,
    ISecretStore secrets,
    ISocialOAuthGateway gateway,
    IProviderAppCache cache,
    IProviderAppValidator validator,
    IInternalQueries queries,
    PlatformAudit audit,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<SocialProviderDto>> ListAsync(CancellationToken cancellationToken)
    {
        await systemAdmins.RequireAsync(cancellationToken);
        var stored = await platform.ListProvidersAsync(cancellationToken);
        var result = new List<SocialProviderDto>();
        foreach (var provider in Enum.GetValues<PlatformProvider>())
            result.Add(await ToDtoAsync(provider, stored.FirstOrDefault(p => p.Provider == provider), cancellationToken));
        return result;
    }

    public async Task<SocialProviderDto> SaveAsync(SaveSocialProviderCommand command, CancellationToken cancellationToken)
    {
        await systemAdmins.RequireAsync(cancellationToken);
        var (config, now, userId) = await LoadOrCreateAsync(command.Provider, cancellationToken);
        if (config.RowVersion.Length > 0)
            ConcurrencyGuard.EnsureCurrent(config.RowVersion, command.ExpectedRowVersion);
        config.Update(command.ClientId, command.RedirectUri, command.ApiVersion, command.ConfigurationId, command.Enabled, userId, now);
        audit.Record(PlatformAuditActions.SocialProviderUpdated, "SocialProvider", command.Provider.ToString(), Label(command.Provider),
            metadata: new Dictionary<string, string?> { ["enabled"] = config.Enabled.ToString(), ["redirectUri"] = config.RedirectUri, ["apiVersion"] = config.ApiVersion, ["configurationId"] = config.ConfigurationId });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await cache.RefreshAsync(cancellationToken);
        return await ToDtoAsync(config.Provider, config, cancellationToken);
    }

    public async Task<SocialProviderDto> ReplaceSecretAsync(PlatformProvider provider, string secret, CancellationToken cancellationToken)
    {
        await systemAdmins.RequireAsync(cancellationToken);
        var value = Guard.Required(secret, provider == PlatformProvider.META ? "Meta App Secret" : "Client Secret", 500);
        var (config, now, userId) = await LoadOrCreateAsync(provider, cancellationToken);
        await secrets.SetAsync(SocialProviderConfiguration.SecretName(provider), value, cancellationToken);
        config.SecretReplaced(userId, now);
        audit.Record(PlatformAuditActions.SocialProviderSecretReplaced, "SocialProvider", provider.ToString(), Label(provider));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await cache.RefreshAsync(cancellationToken);
        return await ToDtoAsync(provider, config, cancellationToken);
    }

    /// <summary>Checks completeness, then asks the provider where it offers a credential check. Nothing is saved but the audit entry.</summary>
    public async Task<ProviderValidationDto> ValidateAsync(PlatformProvider provider, CancellationToken cancellationToken)
    {
        await systemAdmins.RequireAsync(cancellationToken);
        var config = await platform.GetProviderAsync(provider, cancellationToken);
        ProviderValidationDto result;
        if (config is null)
        {
            result = Platforms(provider).Any(gateway.IsConfigured)
                ? new(true, "Using the deployment's environment configuration. Save settings here to manage them from this page.")
                : new(false, "Not configured.");
        }
        else
        {
            var missing = new List<string>();
            if (config.ClientId.Length == 0) missing.Add(provider == PlatformProvider.META ? "App ID" : "Client ID");
            if (!config.HasSecret) missing.Add(provider == PlatformProvider.META ? "App Secret" : "Client Secret");
            if (config.RedirectUri.Length == 0) missing.Add("Redirect URI");
            if (missing.Count > 0)
            {
                result = new(false, $"Missing: {string.Join(", ", missing)}.");
            }
            else
            {
                var secret = await secrets.GetAsync(SocialProviderConfiguration.SecretName(provider), cancellationToken) ?? "";
                var check = await validator.ValidateAsync(provider, config.ClientId, secret, cancellationToken);
                result = check is { } c
                    ? new(c.Valid, c.Message)
                    : new(true, "Configuration is complete. This provider has no credential check; the first customer connection confirms it.");
            }
        }

        audit.Record(PlatformAuditActions.SocialProviderValidated, "SocialProvider", provider.ToString(), Label(provider), succeeded: result.Valid);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    private async Task<(SocialProviderConfiguration Config, DateTime Now, Guid UserId)> LoadOrCreateAsync(PlatformProvider provider, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var userId = currentUser.UserId!.Value;
        if (await platform.GetProviderAsync(provider, cancellationToken) is { } existing)
            return (existing, now, userId);
        var created = SocialProviderConfiguration.Create(provider, userId, now);
        platform.Add(created);
        return (created, now, userId);
    }

    private async Task<SocialProviderDto> ToDtoAsync(PlatformProvider provider, SocialProviderConfiguration? config, CancellationToken cancellationToken)
    {
        var activity = await queries.ProviderActivityAsync(Platforms(provider), timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
        if (config is null)
        {
            var fromEnvironment = Platforms(provider).Any(gateway.IsConfigured);
            return new SocialProviderDto(provider, fromEnvironment ? "ENVIRONMENT" : "NONE", "", "", null, null, null, fromEnvironment, fromEnvironment, null, null, null,
                fromEnvironment ? "CONFIGURED" : "NOT_CONFIGURED", activity.LastSuccessAtUtc, activity.LastFailureAtUtc, activity.LastFailure,
                activity.FailuresLast24Hours);
        }

        var complete = config.ClientId.Length > 0 && config.HasSecret && config.RedirectUri.Length > 0;
        var status = !complete ? "INCOMPLETE" : !config.Enabled ? "DISABLED" : "CONFIGURED";
        return new SocialProviderDto(provider, "PLATFORM", config.ClientId, config.RedirectUri,
            provider == PlatformProvider.META ? InstagramRedirect(config.RedirectUri) : null, config.ApiVersion, config.ConfigurationId, config.Enabled, config.HasSecret,
            config.SecretUpdatedAtUtc, config.UpdatedAtUtc, config.RowVersion.Length > 0 ? Convert.ToBase64String(config.RowVersion) : null, status,
            activity.LastSuccessAtUtc, activity.LastFailureAtUtc, activity.LastFailure, activity.FailuresLast24Hours);
    }

    // Google sign-in is not a social publishing provider, so it lives in the secret store only (no provider row).
    public const string GoogleClientIdSecret = "platform/google-sign-in/client-id";
    public const string GoogleClientSecretSecret = "platform/google-sign-in/client-secret";

    public async Task<GoogleSignInDto> GoogleSignInAsync(CancellationToken cancellationToken)
    {
        await systemAdmins.RequireAsync(cancellationToken);
        return GoogleDto();
    }

    /// <summary>Saves the Google OAuth client; a blank secret keeps the stored one. Takes effect on the next sign-in, no restart.</summary>
    public async Task<GoogleSignInDto> SaveGoogleSignInAsync(string clientId, string? clientSecret, CancellationToken cancellationToken)
    {
        await systemAdmins.RequireAsync(cancellationToken);
        var id = Guard.Required(clientId, "Google Client ID", 200);
        var replaceSecret = !string.IsNullOrWhiteSpace(clientSecret);
        if (!replaceSecret && cache.GoogleSignIn is not { FromPlatform: true })
            throw Guard.Invalid("Google Client Secret", "is required");
        await secrets.SetAsync(GoogleClientIdSecret, id, cancellationToken);
        if (replaceSecret)
            await secrets.SetAsync(GoogleClientSecretSecret, Guard.Required(clientSecret, "Google Client Secret", 500), cancellationToken);
        audit.Record(PlatformAuditActions.GoogleSignInUpdated, "SocialProvider", "GOOGLE", "Google sign-in",
            metadata: new Dictionary<string, string?> { ["rotated"] = replaceSecret.ToString() });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await cache.RefreshAsync(cancellationToken);
        return GoogleDto();
    }

    private GoogleSignInDto GoogleDto() => cache.GoogleSignIn is { } app
        ? new(app.FromPlatform ? "PLATFORM" : "ENVIRONMENT", app.ClientId, app.ClientSecret.Length > 0)
        : new("NONE", "", false);

    /// <summary>Meta serves Facebook and Instagram with one app; Instagram's OAuth callback is the Facebook one with its own platform segment.</summary>
    public static string? InstagramRedirect(string facebookRedirect) =>
        facebookRedirect.Contains("/oauth/facebook/", StringComparison.OrdinalIgnoreCase)
            ? facebookRedirect.Replace("/oauth/facebook/", "/oauth/instagram/", StringComparison.OrdinalIgnoreCase)
            : null;

    public static IReadOnlyList<SocialPlatform> Platforms(PlatformProvider provider) =>
        provider == PlatformProvider.LINKEDIN ? [SocialPlatform.LinkedIn] : [SocialPlatform.Facebook, SocialPlatform.Instagram];

    private static string Label(PlatformProvider provider) => provider == PlatformProvider.LINKEDIN ? "LinkedIn" : "Meta";
}
