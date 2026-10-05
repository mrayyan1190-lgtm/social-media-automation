using System.Text;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Application.Abstractions;

/// <summary>Where and how to call the hosted LLM. ToString is redacted so the key cannot reach a log by accident.</summary>
/// <remarks>MaxOutputTokens, TimeoutSeconds and FallbackModel come from the platform AI configuration (null = defaults).</remarks>
public sealed record LlmConnection(
    AiProvider Provider, string BaseUrl, string Model, string ApiKey, int? MaxOutputTokens = null, int? TimeoutSeconds = null, string? FallbackModel = null)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"Provider = {Provider}, BaseUrl = {BaseUrl}, Model = {Model}, FallbackModel = {FallbackModel}, ApiKey = ***");
        return true;
    }
}

public sealed record LlmChatRequest(string SystemPrompt, string UserContent, double Temperature, int MaxOutputTokens);

public sealed record LlmChatResponse(string Content, string? ResponseModel, int? InputTokens, int? OutputTokens, int LatencyMs, int HttpStatus);

public enum LlmErrorKind
{
    /// <summary>Network failure, timeout, throttling or provider 5xx: safe to retry later.</summary>
    Transient,

    /// <summary>The key was rejected (401/403): retrying will not help.</summary>
    Authentication,

    /// <summary>The provider refused the request (bad model name, invalid parameters).</summary>
    Rejected,
}

public sealed class LlmException(LlmErrorKind kind, string message, int? httpStatus, int latencyMs, string? diagnostic) : Exception(message)
{
    public LlmErrorKind Kind { get; } = kind;
    public int? HttpStatus { get; } = httpStatus;
    public int LatencyMs { get; } = latencyMs;
    public string? Diagnostic { get; } = diagnostic;
}

/// <summary>A chat-completions client that asks for a JSON object response.</summary>
public interface ILlmClient
{
    Task<LlmChatResponse> CompleteJsonAsync(LlmConnection connection, LlmChatRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListModelsAsync(LlmConnection connection, CancellationToken cancellationToken);
}
