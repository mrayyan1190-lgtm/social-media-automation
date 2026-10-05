using SocialMediaAutomation.Domain.Common;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Domain.Entities;

/// <summary>
/// The workspace's hosted-LLM connection, configured by an Admin. The API key is stored only in protected
/// (encrypted) form and is never returned to the browser, placed in prompts, or logged; only its last four
/// characters are kept in clear as a hint.
/// </summary>
public sealed class AiProviderSettings
{
    public const int ModelMax = 200;

    private AiProviderSettings() { }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public AiProvider Provider { get; private set; }
    public string BaseUrl { get; private set; } = null!;
    public string Model { get; private set; } = null!;
    public string ProtectedApiKey { get; private set; } = null!;
    public string ApiKeyHint { get; private set; } = null!;
    public Guid UpdatedByUserId { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static AiProviderSettings Create(
        Guid id, Guid organizationId, AiProvider provider, string? customBaseUrl, string model, string protectedApiKey, string apiKeyHint,
        Guid userId, DateTime utcNow)
    {
        var settings = new AiProviderSettings { Id = id, OrganizationId = organizationId };
        settings.Update(provider, customBaseUrl, model, protectedApiKey, apiKeyHint, userId, utcNow);
        return settings;
    }

    /// <summary>Pass null key values to keep the stored key.</summary>
    public void Update(AiProvider provider, string? customBaseUrl, string model, string? protectedApiKey, string? apiKeyHint, Guid userId, DateTime utcNow)
    {
        Provider = Guard.Defined(provider, "Provider");
        BaseUrl = ResolveBaseUrl(provider, customBaseUrl);
        Model = Guard.Required(model, "Model", ModelMax);
        if (protectedApiKey is not null)
        {
            ProtectedApiKey = protectedApiKey;
            ApiKeyHint = apiKeyHint ?? "";
        }
        else if (ProtectedApiKey is null)
        {
            throw Guard.Invalid("API key", "is required");
        }
        UpdatedByUserId = userId;
        UpdatedAtUtc = utcNow;
    }

    /// <summary>
    /// Known providers always use their official endpoint. A custom endpoint must be https so keys are never sent in
    /// clear text over a network; plain http is accepted only for loopback hosts (local model servers such as Ollama).
    /// ponytail: no private-network (SSRF) filtering yet; acceptable because only Admins can set it.
    /// </summary>
    public static string ResolveBaseUrl(AiProvider provider, string? customBaseUrl) => provider switch
    {
        AiProvider.OpenAI => "https://api.openai.com/v1",
        AiProvider.Groq => "https://api.groq.com/openai/v1",
        AiProvider.OpenAICompatible => Guard.OptionalHttpUrl(customBaseUrl, "Base URL") is { } url
            && Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.IsLoopback)
            ? url.TrimEnd('/')
            : throw Guard.Invalid("Base URL", "must be an https URL (plain http is allowed only for localhost)"),
        _ => throw Guard.Invalid("Provider", "is not supported"),
    };
}
