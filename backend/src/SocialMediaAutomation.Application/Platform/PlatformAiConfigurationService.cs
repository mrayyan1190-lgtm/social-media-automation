using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.AiGeneration.Services;
using SocialMediaAutomation.Application.Common;
using SocialMediaAutomation.Application.Common.Authorization;
using SocialMediaAutomation.Domain.Common;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Application.Platform;

/// <summary>What /internal/ai shows. Never carries the key: only whether one is configured and when it was replaced.</summary>
public sealed record AiConfigurationDto(
    string Source, AiProvider Provider, string? BaseUrl, string? Model, string? FallbackModel, int? MaxOutputTokens, int? RequestTimeoutSeconds,
    bool Enabled, bool ApiKeyConfigured, DateTime? ApiKeyUpdatedAtUtc, DateTime? UpdatedAtUtc, string? RowVersion, AiStatusDto Status,
    AiImageConfigurationDto Image);

/// <summary>Image generation settings. The key is never returned: OwnApiKey says whether a separate image key is stored.</summary>
public sealed record AiImageConfigurationDto(
    bool Enabled, AiProvider? Provider, string? BaseUrl, string? Model, string? Size, IReadOnlyList<string> Sizes, bool OwnApiKey,
    DateTime? ApiKeyUpdatedAtUtc, bool Usable, AiStatusDto Status);

public sealed record SaveAiImageConfigurationCommand(AiProvider Provider, string? BaseUrl, string Model, string Size, bool Enabled, byte[]? ExpectedRowVersion);

/// <summary>Configured: CONFIGURED/NOT_CONFIGURED. Health: HEALTHY, DEGRADED, UNAVAILABLE, NOT_CONFIGURED or NOT_MONITORED.</summary>
public sealed record AiStatusDto(
    string Configured, string Health, string? DefaultModel, DateTime? LastSuccessAtUtc, DateTime? LastFailureAtUtc, string? LastFailure,
    int RequestsToday, int FailedToday);

public sealed record SaveAiConfigurationCommand(
    AiProvider Provider, string? BaseUrl, string Model, string? FallbackModel, int? MaxOutputTokens, int? RequestTimeoutSeconds, bool Enabled,
    byte[]? ExpectedRowVersion);

public sealed record AiConnectionTestDto(bool Succeeded, string Message, bool ModelAvailable, int ModelCount);

/// <summary>
/// The system LLM. Platform settings (stored here, key in the secret store) take precedence over the deployment's
/// environment configuration; customers never see or set any of it.
/// </summary>
public sealed class PlatformAiConfigurationService(
    SystemAdminAuthorizer systemAdmins,
    IPlatformRepository platform,
    ISecretStore secrets,
    AiConnectionResolver resolver,
    ILlmClient llm,
    IInternalQueries queries,
    PlatformAudit audit,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<AiConfigurationDto> GetAsync(CancellationToken cancellationToken)
    {
        await systemAdmins.RequireAsync(cancellationToken);
        var stored = await platform.GetAiConfigurationAsync(cancellationToken);
        var effective = await resolver.GetEffectiveAsync(cancellationToken);
        var activity = await queries.AiActivityAsync(timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
        return new AiConfigurationDto(effective.Source, effective.Provider, effective.BaseUrl, effective.Model, effective.FallbackModel,
            effective.MaxOutputTokens, effective.RequestTimeoutSeconds, effective.Enabled, effective.ApiKeyConfigured, stored?.ApiKeyUpdatedAtUtc,
            stored?.UpdatedAtUtc, stored is null ? null : Convert.ToBase64String(stored.RowVersion), Status(effective, activity),
            await ImageAsync(stored, cancellationToken));
    }

    public async Task<AiConfigurationDto> SaveAsync(SaveAiConfigurationCommand command, CancellationToken cancellationToken)
    {
        await systemAdmins.RequireAsync(cancellationToken);
        var userId = currentUser.UserId!.Value;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var config = await LoadOrCreateAsync(userId, now, cancellationToken);
        if (config.RowVersion.Length > 0)
            ConcurrencyGuard.EnsureCurrent(config.RowVersion, command.ExpectedRowVersion);

        var before = (config.Provider, config.Model, config.FallbackModel, config.Enabled);
        config.Update(command.Provider, command.BaseUrl, command.Model, command.FallbackModel, command.MaxOutputTokens, command.RequestTimeoutSeconds,
            command.Enabled, userId, now);

        if (before.Model != config.Model || before.FallbackModel != config.FallbackModel)
            audit.Record(PlatformAuditActions.AiModelChanged, "AiConfiguration", "platform", "AI / LLM", metadata: new Dictionary<string, string?>
            {
                ["previousModel"] = before.Model, ["model"] = config.Model, ["fallbackModel"] = config.FallbackModel,
            });
        audit.Record(PlatformAuditActions.AiConfigurationChanged, "AiConfiguration", "platform", "AI / LLM", metadata: new Dictionary<string, string?>
        {
            ["provider"] = config.Provider.ToString(), ["enabled"] = config.Enabled.ToString(),
            ["outputLimit"] = config.MaxOutputTokens?.ToString(), ["requestTimeoutSeconds"] = config.RequestTimeoutSeconds?.ToString(),
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await GetAsync(cancellationToken);
    }

    /// <summary>Stores a new key in the secret store. The key is never returned or logged; the response only says one is configured.</summary>
    public async Task<AiConfigurationDto> ReplaceApiKeyAsync(string apiKey, CancellationToken cancellationToken)
    {
        await systemAdmins.RequireAsync(cancellationToken);
        var key = Guard.Required(apiKey, "API key", 500);
        var userId = currentUser.UserId!.Value;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var config = await LoadOrCreateAsync(userId, now, cancellationToken);

        await secrets.SetAsync(PlatformAiConfiguration.ApiKeySecretName, key, cancellationToken);
        config.ApiKeyReplaced(userId, now);
        audit.Record(PlatformAuditActions.AiApiKeyReplaced, "AiConfiguration", "platform", "AI / LLM");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await GetAsync(cancellationToken);
    }

    /// <summary>
    /// Calls the provider's model list with the given key (or the stored one) and checks the model is offered.
    /// Read-only: nothing is saved except the audit entry.
    /// </summary>
    public async Task<AiConnectionTestDto> TestAsync(AiProvider provider, string? baseUrl, string? model, string? apiKey, CancellationToken cancellationToken)
    {
        await systemAdmins.RequireAsync(cancellationToken);
        var key = string.IsNullOrWhiteSpace(apiKey) ? await resolver.PlatformApiKeyAsync(cancellationToken) : apiKey.Trim();
        if (string.IsNullOrWhiteSpace(key))
            throw Guard.Invalid("API key", "is required to test the connection");

        AiConnectionTestDto result;
        try
        {
            var models = await llm.ListModelsAsync(new LlmConnection(provider, AiProviderSettings.ResolveBaseUrl(provider, baseUrl), model ?? "", key), cancellationToken);
            var available = string.IsNullOrWhiteSpace(model) || models.Contains(model.Trim(), StringComparer.Ordinal);
            result = new AiConnectionTestDto(true,
                available ? $"Connected. {models.Count} model(s) available." : $"Connected, but the provider does not offer \"{model}\" to this key.",
                available, models.Count);
        }
        catch (LlmException ex)
        {
            result = new AiConnectionTestDto(false, ex.Kind switch
            {
                LlmErrorKind.Authentication => "The provider rejected the API key.",
                LlmErrorKind.Rejected => $"The provider rejected the request: {SecretRedactor.Redact(ex.Message)}",
                _ => $"The provider could not be reached: {SecretRedactor.Redact(ex.Message)}",
            }, false, 0);
        }

        audit.Record(PlatformAuditActions.AiConnectionTested, "AiConfiguration", "platform", "AI / LLM", succeeded: result.Succeeded,
            metadata: new Dictionary<string, string?> { ["provider"] = provider.ToString(), ["model"] = model });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    // ---- Image generation ----

    public async Task<AiConfigurationDto> SaveImageAsync(SaveAiImageConfigurationCommand command, CancellationToken cancellationToken)
    {
        await systemAdmins.RequireAsync(cancellationToken);
        var userId = currentUser.UserId!.Value;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var config = await LoadOrCreateAsync(userId, now, cancellationToken);
        if (config.RowVersion.Length > 0)
            ConcurrencyGuard.EnsureCurrent(config.RowVersion, command.ExpectedRowVersion);

        config.UpdateImage(command.Provider, command.BaseUrl, command.Model, command.Size, command.Enabled, userId, now);
        audit.Record(PlatformAuditActions.AiImageConfigurationChanged, "AiConfiguration", "platform-image", "AI image generation",
            metadata: new Dictionary<string, string?>
            {
                ["provider"] = command.Provider.ToString(), ["model"] = config.ImageModel, ["size"] = config.ImageSize, ["enabled"] = config.ImageEnabled.ToString(),
            });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await GetAsync(cancellationToken);
    }

    /// <summary>A separate key for image generation (optional: without one the AI / LLM key is used). Never returned or logged.</summary>
    public async Task<AiConfigurationDto> ReplaceImageApiKeyAsync(string apiKey, CancellationToken cancellationToken)
    {
        await systemAdmins.RequireAsync(cancellationToken);
        var key = Guard.Required(apiKey, "Image API key", 500);
        var userId = currentUser.UserId!.Value;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var config = await LoadOrCreateAsync(userId, now, cancellationToken);
        await secrets.SetAsync(PlatformAiConfiguration.ImageApiKeySecretName, key, cancellationToken);
        config.ImageApiKeyReplaced(userId, now);
        audit.Record(PlatformAuditActions.AiImageApiKeyReplaced, "AiConfiguration", "platform-image", "AI image generation");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await GetAsync(cancellationToken);
    }

    /// <summary>Checks the key and that the image model is offered (model list only: no image is generated, nothing is billed).</summary>
    public async Task<AiConnectionTestDto> TestImageAsync(AiProvider provider, string? baseUrl, string? model, string? apiKey, CancellationToken cancellationToken)
    {
        await systemAdmins.RequireAsync(cancellationToken);
        var stored = await platform.GetAiConfigurationAsync(cancellationToken);
        var key = !string.IsNullOrWhiteSpace(apiKey) ? apiKey.Trim()
            : stored is { HasImageApiKey: true } ? await secrets.GetAsync(PlatformAiConfiguration.ImageApiKeySecretName, cancellationToken)
            : await resolver.PlatformApiKeyAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(key))
            throw Guard.Invalid("Image API key", "is required to test the connection");

        AiConnectionTestDto result;
        try
        {
            var models = await llm.ListModelsAsync(new LlmConnection(provider, AiProviderSettings.ResolveBaseUrl(provider, baseUrl), model ?? "", key), cancellationToken);
            var available = string.IsNullOrWhiteSpace(model) || models.Contains(model.Trim(), StringComparer.Ordinal);
            result = new AiConnectionTestDto(true,
                available ? $"Connected. \"{model}\" is available to this key." : $"Connected, but the provider does not offer \"{model}\" to this key.",
                available, models.Count);
        }
        catch (LlmException ex)
        {
            result = new AiConnectionTestDto(false, ex.Kind == LlmErrorKind.Authentication
                ? "The provider rejected the API key."
                : $"The provider could not be reached or refused the request: {SecretRedactor.Redact(ex.Message)}", false, 0);
        }

        audit.Record(PlatformAuditActions.AiImageConnectionTested, "AiConfiguration", "platform-image", "AI image generation", succeeded: result.Succeeded,
            metadata: new Dictionary<string, string?> { ["provider"] = provider.ToString(), ["model"] = model });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    private async Task<AiImageConfigurationDto> ImageAsync(PlatformAiConfiguration? stored, CancellationToken cancellationToken)
    {
        var activity = await queries.AiActivityAsync(timeProvider.GetUtcNow().UtcDateTime, cancellationToken, images: true);
        var usable = await resolver.ResolveImageAsync(cancellationToken) is not null;
        var health = !usable ? HealthStatusCodes.NotConfigured
            : activity.CallsLastHour == 0 ? HealthStatusCodes.NotMonitored
            : activity.FailedCallsLastHour == activity.CallsLastHour ? HealthStatusCodes.Unavailable
            : activity.FailedCallsLastHour > 0 ? HealthStatusCodes.Degraded
            : HealthStatusCodes.Healthy;
        return new AiImageConfigurationDto(stored?.ImageEnabled ?? false, stored?.ImageProvider, stored?.ImageBaseUrl, stored?.ImageModel, stored?.ImageSize,
            PlatformAiConfiguration.ImageSizes, stored?.HasImageApiKey ?? false, stored?.ImageApiKeyUpdatedAtUtc, usable,
            new AiStatusDto(usable ? "CONFIGURED" : "NOT_CONFIGURED", health, stored?.ImageModel, activity.LastSuccessAtUtc, activity.LastFailureAtUtc,
                activity.LastFailure, activity.RequestsToday, activity.FailedToday));
    }

    private async Task<PlatformAiConfiguration> LoadOrCreateAsync(Guid userId, DateTime now, CancellationToken cancellationToken)
    {
        if (await platform.GetAiConfigurationAsync(cancellationToken) is { } existing)
            return existing;
        var created = PlatformAiConfiguration.Create(userId, now);
        platform.Add(created);
        return created;
    }

    private static AiStatusDto Status(EffectiveAiConfiguration config, AiActivityDto activity)
    {
        // Health comes from real calls in the last hour; with no recent calls there is nothing to judge (not monitored).
        var health = !config.IsUsable ? HealthStatusCodes.NotConfigured
            : activity.CallsLastHour == 0 ? HealthStatusCodes.NotMonitored
            : activity.FailedCallsLastHour == activity.CallsLastHour ? HealthStatusCodes.Unavailable
            : activity.FailedCallsLastHour > 0 ? HealthStatusCodes.Degraded
            : HealthStatusCodes.Healthy;
        return new AiStatusDto(config.IsUsable ? "CONFIGURED" : "NOT_CONFIGURED", health, config.Model, activity.LastSuccessAtUtc,
            activity.LastFailureAtUtc, activity.LastFailure, activity.RequestsToday, activity.FailedToday);
    }
}

public static class HealthStatusCodes
{
    public const string Healthy = "HEALTHY";
    public const string Degraded = "DEGRADED";
    public const string Unavailable = "UNAVAILABLE";
    public const string NotConfigured = "NOT_CONFIGURED";
    public const string NotMonitored = "NOT_MONITORED";
}
