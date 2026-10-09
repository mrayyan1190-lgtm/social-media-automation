using System.Text.Json;
using SocialMediaAutomation.Domain.Common;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Domain.Entities;

// Platform-level records owned by the SaaS operator (system administrators), never by a workspace. None of them holds a
// secret value: secrets live in the secret store and are referenced by name only.

/// <summary>The system LLM used by every workspace. One row. The API key is referenced by secret name, never stored here.</summary>
public sealed class PlatformAiConfiguration
{
    public static readonly Guid SingletonId = new("00000000-0000-0000-0000-00000000a1a1");
    public const string ApiKeySecretName = "platform/ai/api-key";
    public const int MaxTokensLimit = 32000;
    public const int MaxTimeoutSeconds = 600;

    private PlatformAiConfiguration() { }

    public Guid Id { get; private set; }
    public AiProvider Provider { get; private set; }
    public string? BaseUrl { get; private set; }
    public string Model { get; private set; } = null!;
    public string? FallbackModel { get; private set; }
    public int? MaxOutputTokens { get; private set; }
    public int? RequestTimeoutSeconds { get; private set; }
    public bool Enabled { get; private set; }
    /// <summary>Name of the API key in the secret store; null until a key has been saved.</summary>
    public string? ApiKeySecretRef { get; private set; }
    public DateTime? ApiKeyUpdatedAtUtc { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public bool HasApiKey => ApiKeySecretRef is not null;

    public static PlatformAiConfiguration Create(Guid userId, DateTime utcNow) =>
        new() { Id = SingletonId, Provider = AiProvider.OpenAI, Model = "", UpdatedByUserId = userId, UpdatedAtUtc = utcNow };

    public void Update(AiProvider provider, string? baseUrl, string model, string? fallbackModel, int? maxOutputTokens, int? timeoutSeconds,
        bool enabled, Guid userId, DateTime utcNow)
    {
        Provider = Guard.Defined(provider, "Provider").IsImageOnly()
            ? throw Guard.Invalid("Provider", $"{provider} is used only for image generation; choose a text model provider")
            : provider;
        BaseUrl = provider == AiProvider.OpenAICompatible ? AiProviderSettings.ResolveBaseUrl(provider, baseUrl) : null;
        Model = Guard.Required(model, "Default model", AiProviderSettings.ModelMax);
        FallbackModel = Guard.Optional(fallbackModel, "Fallback model", AiProviderSettings.ModelMax);
        MaxOutputTokens = maxOutputTokens is { } tokens
            ? tokens is >= 256 and <= MaxTokensLimit ? tokens : throw Guard.Invalid("Max output tokens", $"must be between 256 and {MaxTokensLimit}")
            : null;
        RequestTimeoutSeconds = timeoutSeconds is { } seconds
            ? seconds is >= 5 and <= MaxTimeoutSeconds ? seconds : throw Guard.Invalid("Request timeout", $"must be between 5 and {MaxTimeoutSeconds} seconds")
            : null;
        if (enabled && !HasApiKey)
            throw Guard.Invalid("API key", "is required before AI can be enabled");
        Enabled = enabled;
        UpdatedByUserId = userId;
        UpdatedAtUtc = utcNow;
    }

    public void ApiKeyReplaced(Guid userId, DateTime utcNow)
    {
        ApiKeySecretRef = ApiKeySecretName;
        ApiKeyUpdatedAtUtc = utcNow;
        UpdatedByUserId = userId;
        UpdatedAtUtc = utcNow;
    }

    // ---- Image generation (an OpenAI-compatible images endpoint, Hugging Face text-to-image, or Stability AI) ----

    public const string ImageApiKeySecretName = "platform/ai/image-api-key";

    /// <summary>
    /// Sizes every model accepts that also suit every platform: square, or landscape within Instagram's 1.91:1 limit.
    /// (Portrait sizes such as 1024x1536 are taller than Instagram's 4:5 limit.)
    /// </summary>
    public static readonly string[] ImageSizes = ["1024x1024", "1536x1024", "1792x1024"];

    /// <summary>Stability AI's Stable Image endpoints; "core" (Stable Image Core) is the default.</summary>
    public static readonly string[] StabilityImageModels = ["core", "ultra", "sd3"];

    /// <summary>Cloudflare Workers AI text-to-image model offered here: FLUX.1 [schnell] (JPEG, 1024x1024).</summary>
    public static readonly string[] CloudflareImageModels = ["@cf/black-forest-labs/flux-1-schnell"];

    public bool ImageEnabled { get; private set; }
    public AiProvider? ImageProvider { get; private set; }
    /// <summary>OpenAI-compatible endpoint, or the Cloudflare account URL (holds the Account ID).</summary>
    public string? ImageBaseUrl { get; private set; }
    public string? ImageModel { get; private set; }
    public string? ImageSize { get; private set; }
    /// <summary>Optional separate key; when absent the LLM key is used (same provider account).</summary>
    public string? ImageApiKeySecretRef { get; private set; }
    public DateTime? ImageApiKeyUpdatedAtUtc { get; private set; }

    public bool HasImageApiKey => ImageApiKeySecretRef is not null;

    /// <summary>
    /// The LLM key belongs to the LLM provider's account, so a Hugging Face image model can only borrow a Hugging Face LLM key,
    /// and image-only providers (Stability AI, Cloudflare) always need their own image key.
    /// </summary>
    public bool ImageCanUseLlmKey => ImageProvider switch
    {
        AiProvider.HuggingFace => Provider == AiProvider.HuggingFace,
        { } p when p.IsImageOnly() => false,
        _ => true,
    };

    public void UpdateImage(AiProvider provider, string? baseUrl, string model, string size, bool enabled, Guid userId, DateTime utcNow)
    {
        if (Guard.Defined(provider, "Image provider") == AiProvider.Groq)
            throw Guard.Invalid("Image provider", "Groq does not offer image generation; use OpenAI, Hugging Face, Stability AI, Cloudflare Workers AI or an OpenAI-compatible endpoint");
        ImageProvider = provider;
        ImageBaseUrl = provider is AiProvider.OpenAICompatible or AiProvider.CloudflareWorkersAI ? AiProviderSettings.ResolveBaseUrl(provider, baseUrl) : null;
        ImageModel = Guard.Required(model, "Image model", AiProviderSettings.ModelMax);
        if (provider == AiProvider.StabilityAI && !StabilityImageModels.Contains(ImageModel))
            throw Guard.Invalid("Image model", $"must be one of {string.Join(", ", StabilityImageModels)} for Stability AI");
        if (provider == AiProvider.CloudflareWorkersAI && !CloudflareImageModels.Contains(ImageModel))
            throw Guard.Invalid("Image model", $"must be {string.Join(", ", CloudflareImageModels)} for Cloudflare Workers AI");
        ImageSize = ImageSizes.Contains(size) ? size : throw Guard.Invalid("Image size", $"must be one of {string.Join(", ", ImageSizes)}");
        if (enabled && !HasImageApiKey && !(HasApiKey && ImageCanUseLlmKey))
            throw Guard.Invalid("Image API key", ImageCanUseLlmKey
                ? "is required (or save an AI / LLM API key) before image generation can be enabled"
                : provider == AiProvider.StabilityAI
                    ? "is required: a Stability AI key (sk-…) must be saved as the image API key"
                : provider == AiProvider.CloudflareWorkersAI
                    ? "is required: a Cloudflare Workers AI API token must be saved as the image API key"
                    : "is required: a Hugging Face token (hf_…) must be saved as the image API key");
        ImageEnabled = enabled;
        UpdatedByUserId = userId;
        UpdatedAtUtc = utcNow;
    }

    // ---- Image understanding (Hugging Face vision models: its own token, or the LLM key when the LLM is Hugging Face) ----

    public const string VisionApiKeySecretName = "platform/ai/vision-api-key";

    /// <summary>Optional token for image understanding only; when absent the LLM key is used if the LLM is Hugging Face.</summary>
    public string? VisionApiKeySecretRef { get; private set; }
    public DateTime? VisionApiKeyUpdatedAtUtc { get; private set; }

    public bool HasVisionApiKey => VisionApiKeySecretRef is not null;

    /// <summary>On unless an operator turns it off, so a saved Hugging Face token keeps working; null model = catalog default.</summary>
    public bool VisionEnabled { get; private set; } = true;
    public string? VisionModel { get; private set; }

    /// <summary>
    /// The Hugging Face token image understanding uses: its own vision token when saved, else the LLM key (LLM on Hugging
    /// Face). Never the image-generation key: vision and image generation are billed and rotated separately.
    /// </summary>
    public string? HuggingFaceTokenSecretName =>
        HasVisionApiKey ? VisionApiKeySecretName
        : Provider == AiProvider.HuggingFace && HasApiKey ? ApiKeySecretName
        : null;

    /// <summary>The caller checks the model against the offered list.</summary>
    public void UpdateVision(string model, bool enabled, Guid userId, DateTime utcNow)
    {
        VisionModel = Guard.Required(model, "Vision model", AiProviderSettings.ModelMax);
        VisionEnabled = enabled;
        UpdatedByUserId = userId;
        UpdatedAtUtc = utcNow;
    }

    public void VisionApiKeyReplaced(Guid userId, DateTime utcNow)
    {
        VisionApiKeySecretRef = VisionApiKeySecretName;
        VisionApiKeyUpdatedAtUtc = utcNow;
        UpdatedByUserId = userId;
        UpdatedAtUtc = utcNow;
    }

    public void ImageApiKeyReplaced(Guid userId, DateTime utcNow)
    {
        ImageApiKeySecretRef = ImageApiKeySecretName;
        ImageApiKeyUpdatedAtUtc = utcNow;
        UpdatedByUserId = userId;
        UpdatedAtUtc = utcNow;
    }
}

/// <summary>Platform integrations (the operator's own provider apps). Not customer social accounts.</summary>
public enum PlatformProvider
{
    LINKEDIN,
    META,
}

/// <summary>One provider app (LinkedIn, or Meta for Facebook and Instagram). The client secret is referenced by name only.</summary>
public sealed class SocialProviderConfiguration
{
    private SocialProviderConfiguration() { }

    public PlatformProvider Provider { get; private set; }
    public string ClientId { get; private set; } = "";
    public string RedirectUri { get; private set; } = "";
    public string? ApiVersion { get; private set; }
    /// <summary>Meta only: a Facebook Login for Business configuration; when set it replaces the requested scopes.</summary>
    public string? ConfigurationId { get; private set; }
    public bool Enabled { get; private set; }
    public string? ClientSecretRef { get; private set; }
    public DateTime? SecretUpdatedAtUtc { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public bool HasSecret => ClientSecretRef is not null;

    public static string SecretName(PlatformProvider provider) => $"platform/social/{provider.ToString().ToLowerInvariant()}/client-secret";

    public static SocialProviderConfiguration Create(PlatformProvider provider, Guid userId, DateTime utcNow) =>
        new() { Provider = Guard.Defined(provider, "Provider"), UpdatedByUserId = userId, UpdatedAtUtc = utcNow };

    public void Update(string clientId, string? redirectUri, string? apiVersion, string? configurationId, bool enabled, Guid userId, DateTime utcNow)
    {
        ClientId = Guard.Required(clientId, Provider == PlatformProvider.META ? "Meta App ID" : "Client ID", 200);
        RedirectUri = Guard.OptionalHttpUrl(redirectUri, "Redirect URI") ?? "";
        ApiVersion = Guard.Optional(apiVersion, "API version", 20);
        ConfigurationId = Provider == PlatformProvider.META ? Guard.Optional(configurationId, "Meta Configuration ID", 100) : null;
        if (enabled && !HasSecret)
            throw Guard.Invalid(Provider == PlatformProvider.META ? "Meta App Secret" : "Client Secret", "is required before the provider can be enabled");
        Enabled = enabled;
        UpdatedByUserId = userId;
        UpdatedAtUtc = utcNow;
    }

    public void SecretReplaced(Guid userId, DateTime utcNow)
    {
        ClientSecretRef = SecretName(Provider);
        SecretUpdatedAtUtc = utcNow;
        UpdatedByUserId = userId;
        UpdatedAtUtc = utcNow;
    }
}

/// <summary>
/// The development/MVP secret store: values encrypted with the shared data-protection key ring. A managed store (e.g.
/// Azure Key Vault) replaces this table behind the same abstraction; nothing else references secret values.
/// </summary>
public sealed class PlatformSecret
{
    private PlatformSecret() { }

    public string Name { get; private set; } = null!;
    public string ProtectedValue { get; private set; } = null!;
    public DateTime UpdatedAtUtc { get; private set; }

    public static PlatformSecret Create(string name, string protectedValue, DateTime utcNow) =>
        new() { Name = name, ProtectedValue = protectedValue, UpdatedAtUtc = utcNow };

    public void Replace(string protectedValue, DateTime utcNow)
    {
        ProtectedValue = protectedValue;
        UpdatedAtUtc = utcNow;
    }
}

public static class PlatformAuditActions
{
    public const string AdminSignedIn = "ADMIN_SIGNED_IN";
    public const string AdminSignedOut = "ADMIN_SIGNED_OUT";
    public const string AiConfigurationChanged = "AI_CONFIGURATION_CHANGED";
    public const string AiApiKeyReplaced = "AI_API_KEY_REPLACED";
    public const string AiModelChanged = "AI_MODEL_CHANGED";
    public const string AiConnectionTested = "AI_CONNECTION_TESTED";
    public const string AiImageConfigurationChanged = "AI_IMAGE_CONFIGURATION_CHANGED";
    public const string AiImageApiKeyReplaced = "AI_IMAGE_API_KEY_REPLACED";
    public const string AiImageConnectionTested = "AI_IMAGE_CONNECTION_TESTED";
    public const string AiVisionConfigurationChanged = "AI_VISION_CONFIGURATION_CHANGED";
    public const string AiVisionConnectionTested = "AI_VISION_CONNECTION_TESTED";
    public const string AiVisionApiKeyReplaced = "AI_VISION_API_KEY_REPLACED";
    public const string SocialProviderUpdated = "SOCIAL_PROVIDER_UPDATED";
    public const string SocialProviderSecretReplaced = "SOCIAL_PROVIDER_SECRET_REPLACED";
    public const string SocialProviderValidated = "SOCIAL_PROVIDER_VALIDATED";
    public const string GoogleSignInUpdated = "GOOGLE_SIGN_IN_UPDATED";
    public const string FacebookPageTokenConnected = "FACEBOOK_PAGE_TOKEN_CONNECTED";
    public const string WorkspaceSuspended = "WORKSPACE_SUSPENDED";
    public const string WorkspaceReactivated = "WORKSPACE_REACTIVATED";
    public const string UserDisabled = "USER_DISABLED";
    public const string UserEnabled = "USER_ENABLED";
    public const string ReconciliationChecked = "RECONCILIATION_CHECKED";
    public const string ReconciliationConfirmedPublished = "RECONCILIATION_CONFIRMED_PUBLISHED";
    public const string ReconciliationConfirmedFailed = "RECONCILIATION_CONFIRMED_FAILED";
}

/// <summary>
/// Audit of platform-operator actions (configuration, tenants, reconciliation). Same safety rule as the workspace audit:
/// identifiers and states only, never secrets, tokens or credentials.
/// </summary>
public sealed class PlatformAuditLog
{
    private static readonly string[] ForbiddenKeyFragments = ["token", "secret", "password", "apikey", "api_key", "authorization", "credential", "key"];

    private PlatformAuditLog() { }

    public Guid Id { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public string Action { get; private set; } = null!;
    public string TargetType { get; private set; } = null!;
    public string TargetId { get; private set; } = null!;
    public string? TargetLabel { get; private set; }
    public Guid? OrganizationId { get; private set; }
    public string Result { get; private set; } = null!;
    public string? CorrelationId { get; private set; }
    public string MetadataJson { get; private set; } = "{}";
    public DateTime CreatedAtUtc { get; private set; }

    public static PlatformAuditLog Record(
        Guid? actorUserId, string action, string targetType, string targetId, string? targetLabel, Guid? organizationId, bool succeeded,
        string? correlationId, IReadOnlyDictionary<string, string?>? metadata, DateTime utcNow)
    {
        var safe = metadata ?? new Dictionary<string, string?>();
        if (safe.Keys.FirstOrDefault(k => ForbiddenKeyFragments.Any(f => k.Contains(f, StringComparison.OrdinalIgnoreCase))) is { } forbidden)
            throw new InvalidOperationException($"Audit metadata key '{forbidden}' looks like a secret and must not be recorded.");
        return new PlatformAuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = actorUserId,
            Action = action,
            TargetType = targetType,
            TargetId = targetId.Length > 100 ? targetId[..100] : targetId,
            TargetLabel = targetLabel is { Length: > 200 } label ? label[..200] : targetLabel,
            OrganizationId = organizationId,
            Result = succeeded ? "SUCCESS" : "FAILURE",
            CorrelationId = CorrelationIds.Normalize(correlationId),
            MetadataJson = JsonSerializer.Serialize(safe),
            CreatedAtUtc = utcNow,
        };
    }
}

public static class SignInEvents
{
    public const string Succeeded = "LOGIN_SUCCEEDED";
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string AccountDisabled = "ACCOUNT_DISABLED";
    public const string InternalForbidden = "INTERNAL_ACCESS_FORBIDDEN";
    public const string LoggedOut = "LOGOUT";
}

/// <summary>One sign-in, failed sign-in or sign-out. Never stores the password or the session cookie.</summary>
public sealed class SignInEvent
{
    private SignInEvent() { }

    public Guid Id { get; private set; }
    public Guid? UserId { get; private set; }
    public string Email { get; private set; } = null!;
    /// <summary>CUSTOMER or INTERNAL sign-in.</summary>
    public string Area { get; private set; } = null!;
    public string EventType { get; private set; } = null!;
    public bool Succeeded { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public string? CorrelationId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static SignInEvent Record(Guid? userId, string email, string area, string eventType, string? ipAddress, string? userAgent,
        string? correlationId, DateTime utcNow) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Email = Clip(email.Trim(), 256)!,
        Area = area,
        EventType = eventType,
        Succeeded = eventType is SignInEvents.Succeeded or SignInEvents.LoggedOut,
        IpAddress = Clip(ipAddress, 64),
        UserAgent = Clip(userAgent, 300),
        CorrelationId = CorrelationIds.Normalize(correlationId),
        CreatedAtUtc = utcNow,
    };

    private static string? Clip(string? value, int max) => value is { Length: > 0 } v && v.Length > max ? v[..max] : value;
}

public static class SystemEventLevels
{
    public const string Info = "INFO";
    public const string Warning = "WARNING";
    public const string Error = "ERROR";
    public const string Critical = "CRITICAL";
}

/// <summary>An operational event (service start, unhandled error, probe failure). Messages are redacted before storage.</summary>
public sealed class SystemEvent
{
    private SystemEvent() { }

    public Guid Id { get; private set; }
    public string Level { get; private set; } = null!;
    public string Service { get; private set; } = null!;
    public string EventName { get; private set; } = null!;
    public string Message { get; private set; } = null!;
    public string? CorrelationId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static SystemEvent Record(string level, string service, string eventName, string message, string? correlationId, DateTime utcNow) => new()
    {
        Id = Guid.NewGuid(),
        Level = level,
        Service = service.Length > 50 ? service[..50] : service,
        EventName = eventName.Length > 100 ? eventName[..100] : eventName,
        Message = SecretRedactor.Redact(message.Length > 2000 ? message[..2000] : message),
        CorrelationId = CorrelationIds.Normalize(correlationId),
        CreatedAtUtc = utcNow,
    };
}
