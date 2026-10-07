using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Application.AiGeneration.Services;

/// <summary>
/// The system-wide hosted LLM, configured by developers/operators through configuration ("Ai" section or environment
/// variables Ai__Provider, Ai__BaseUrl, Ai__Model, Ai__ApiKey). End users never enter keys or choose models.
/// </summary>
public sealed class SystemAiOptions
{
    public const string SectionName = "Ai";

    /// <summary>OpenAI, Groq or OpenAICompatible. Defaults to OpenAI.</summary>
    public string? Provider { get; set; }

    /// <summary>Only used for OpenAICompatible.</summary>
    public string? BaseUrl { get; set; }

    public string? Model { get; set; }

    /// <summary>Secret: supply through environment/secret configuration, never in committed files.</summary>
    public string? ApiKey { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(Model);
}

/// <summary>Source: PLATFORM (set in /internal/ai), ENVIRONMENT (deployment configuration) or NONE.</summary>
public sealed record EffectiveAiConfiguration(
    string Source, AiProvider Provider, string? BaseUrl, string? Model, string? FallbackModel, int? MaxOutputTokens, int? RequestTimeoutSeconds,
    bool Enabled, bool ApiKeyConfigured)
{
    public bool IsUsable => Enabled && ApiKeyConfigured && !string.IsNullOrWhiteSpace(Model);
}

/// <summary>
/// Resolves which LLM a workspace's AI work uses: the platform configuration set by system administrators (key in the
/// secret store) when it exists; otherwise the deployment's environment configuration; otherwise the legacy
/// per-workspace record (kept as an internal fallback, no longer part of the product UI).
/// </summary>
public sealed class AiConnectionResolver(SystemAiOptions system, IAiRepository ai, ISecretProtector secrets, IPlatformRepository platform, ISecretStore secretStore)
{
    public async Task<EffectiveAiConfiguration> GetEffectiveAsync(CancellationToken cancellationToken)
    {
        if (await platform.GetAiConfigurationAsync(cancellationToken) is { } stored)
            return new EffectiveAiConfiguration("PLATFORM", stored.Provider, stored.BaseUrl, stored.Model, stored.FallbackModel, stored.MaxOutputTokens,
                stored.RequestTimeoutSeconds, stored.Enabled, stored.HasApiKey);
        if (system.IsConfigured)
            return new EffectiveAiConfiguration("ENVIRONMENT", ParseProvider(system.Provider), system.BaseUrl, system.Model, null, null, null, true, true);
        return new EffectiveAiConfiguration("NONE", AiProvider.OpenAI, null, null, null, null, null, false, false);
    }

    /// <summary>The platform key from the secret store, or the environment key. Backend use only.</summary>
    public async Task<string?> PlatformApiKeyAsync(CancellationToken cancellationToken) =>
        await platform.GetAiConfigurationAsync(cancellationToken) is { HasApiKey: true }
            ? await secretStore.GetAsync(PlatformAiConfiguration.ApiKeySecretName, cancellationToken)
            : system.ApiKey;

    /// <summary>
    /// The image model, or null when image generation is not enabled in /internal/ai. Uses the image key when one is saved,
    /// otherwise the AI / LLM key (same provider account). Configured only through platform settings.
    /// </summary>
    public async Task<ImageConnection?> ResolveImageAsync(CancellationToken cancellationToken)
    {
        if (await platform.GetAiConfigurationAsync(cancellationToken) is not { ImageEnabled: true, ImageProvider: { } provider, ImageModel: { Length: > 0 } model } config)
            return null;
        var key = config.HasImageApiKey ? await secretStore.GetAsync(PlatformAiConfiguration.ImageApiKeySecretName, cancellationToken)
            : config.ImageCanUseLlmKey ? await PlatformApiKeyAsync(cancellationToken)
            : null;
        return string.IsNullOrWhiteSpace(key)
            ? null
            : new ImageConnection(provider, AiProviderSettings.ResolveBaseUrl(provider, config.ImageBaseUrl), model, key.Trim(), config.ImageSize ?? "1024x1024");
    }

    /// <summary>
    /// A Hugging Face chat connection for a vision model, using the Hugging Face token already configured in /internal/ai:
    /// the image key when image generation uses Hugging Face, otherwise the AI / LLM key when the LLM is Hugging Face.
    /// Null when no enabled Hugging Face token exists.
    /// </summary>
    public async Task<LlmConnection?> ResolveHuggingFaceAsync(string model, CancellationToken cancellationToken)
    {
        var config = await platform.GetAiConfigurationAsync(cancellationToken);
        var key = config is { VisionEnabled: true, HuggingFaceTokenSecretName: { } secretName }
            ? await secretStore.GetAsync(secretName, cancellationToken)
            : null;
        return string.IsNullOrWhiteSpace(key)
            ? null
            : new LlmConnection(AiProvider.HuggingFace, AiProviderSettings.ResolveBaseUrl(AiProvider.HuggingFace, null), model, key.Trim(),
                config!.MaxOutputTokens, config.RequestTimeoutSeconds ?? AiModelCatalog.VisionTimeoutSeconds);
    }

    /// <summary>The vision models users may pick (operator's default first); empty when image understanding is not usable.</summary>
    public async Task<IReadOnlyList<string>> VisionModelsAsync(CancellationToken cancellationToken)
    {
        var config = await platform.GetAiConfigurationAsync(cancellationToken);
        return config is { VisionEnabled: true, HuggingFaceTokenSecretName: not null }
            ? AiModelCatalog.WithDefault(AiModelCatalog.HuggingFaceVisionModels, AiModelCatalog.VisionModelOrDefault(config.VisionModel))
            : [];
    }

    /// <summary>Null when no LLM is configured for this workspace (or the platform LLM is disabled).</summary>
    public async Task<LlmConnection?> ResolveAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var effective = await GetEffectiveAsync(cancellationToken);
        if (effective.Source != "NONE")
        {
            var key = effective.IsUsable ? await PlatformApiKeyAsync(cancellationToken) : null;
            return string.IsNullOrWhiteSpace(key)
                ? null
                : new LlmConnection(effective.Provider, AiProviderSettings.ResolveBaseUrl(effective.Provider, effective.BaseUrl), effective.Model!.Trim(),
                    key.Trim(), effective.MaxOutputTokens, effective.RequestTimeoutSeconds, effective.FallbackModel);
        }

        var settings = await ai.GetSettingsAsync(organizationId, cancellationToken);
        return settings is null
            ? null
            : new LlmConnection(settings.Provider, settings.BaseUrl, settings.Model, secrets.Unprotect(settings.ProtectedApiKey));
    }

    private static AiProvider ParseProvider(string? value) => Enum.TryParse<AiProvider>(value, ignoreCase: true, out var parsed) ? parsed : AiProvider.OpenAI;
}
