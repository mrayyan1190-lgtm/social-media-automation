using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using SocialMediaAutomation.Application.Abstractions;

namespace SocialMediaAutomation.Infrastructure.Ai;

/// <summary>
/// OpenAI-compatible images endpoint (POST /images/generations). GPT image models are asked for JPEG (Instagram accepts
/// only JPEG); DALL·E models return PNG. The key and prompt are never logged; errors are normalized like chat errors.
/// </summary>
internal sealed class OpenAiImageClient(IHttpClientFactory httpClientFactory, ILlmClient llm) : IImageGenerationProvider
{
    public const string HttpClientName = "image-model";
    private const int DiagnosticMax = 2000;

    public async Task<GeneratedImage> GenerateAsync(ImageConnection connection, string prompt, CancellationToken cancellationToken)
    {
        var body = new JsonObject { ["model"] = connection.Model, ["prompt"] = prompt, ["n"] = 1, ["size"] = connection.Size };
        if (connection.Model.StartsWith("dall-e", StringComparison.OrdinalIgnoreCase))
            body["response_format"] = "b64_json";
        else
            body["output_format"] = "jpeg";

        using var message = new HttpRequestMessage(HttpMethod.Post, $"{connection.BaseUrl.TrimEnd('/')}/images/generations") { Content = JsonContent.Create(body) };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.ApiKey);
        var stopwatch = Stopwatch.StartNew();
        string json;
        int status;
        try
        {
            using var response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(message, cancellationToken);
            status = (int)response.StatusCode;
            json = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new LlmException(LlmErrorKind.Transient, $"The image provider could not be reached ({ex.HttpRequestError}).", null, (int)stopwatch.ElapsedMilliseconds, null);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LlmException(LlmErrorKind.Transient, "The image provider did not respond in time.", null, (int)stopwatch.ElapsedMilliseconds, null);
        }

        var latency = (int)stopwatch.ElapsedMilliseconds;
        ThrowIfFailed(status, json, latency);

        try
        {
            var b64 = JsonNode.Parse(json)?["data"]?[0]?["b64_json"]?.GetValue<string>()
                ?? throw new LlmException(LlmErrorKind.Transient, "The image provider returned no image.", status, latency, null);
            return new GeneratedImage(Convert.FromBase64String(b64), latency, status);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            throw new LlmException(LlmErrorKind.Transient, "The image provider response was not valid.", status, latency, null);
        }
    }

    public async Task<bool> IsModelAvailableAsync(ImageConnection connection, CancellationToken cancellationToken)
    {
        var models = await llm.ListModelsAsync(new LlmConnection(connection.Provider, connection.BaseUrl, connection.Model, connection.ApiKey), cancellationToken);
        return string.IsNullOrWhiteSpace(connection.Model) || models.Contains(connection.Model.Trim(), StringComparer.Ordinal);
    }

    /// <summary>Shared by the image adapters so every image provider's failures get the same retry policy.</summary>
    internal static void ThrowIfFailed(int status, string json, int latency)
    {
        if (status is >= 200 and < 300)
            return;
        var (kind, summary) = status switch
        {
            401 or 403 => (LlmErrorKind.Authentication, "The image provider rejected the API key."),
            408 or 409 or 429 or >= 500 => (LlmErrorKind.Transient, $"The image provider is temporarily unavailable (HTTP {status})."),
            // 400 includes content-policy refusals: not retried, the user can change the instruction.
            _ => (LlmErrorKind.Rejected, $"The image provider refused the request (HTTP {status}): {ProviderMessage(json)}"),
        };
        throw new LlmException(kind, summary, status, latency, Truncate(json));
    }

    /// <summary>OpenAI sends {"error":{"message":…}}; Hugging Face sends {"error":"…"}.</summary>
    private static string ProviderMessage(string body)
    {
        try
        {
            var error = JsonNode.Parse(body)?["error"];
            return (error is JsonValue ? error.GetValue<string>() : error?["message"]?.GetValue<string>()) ?? Truncate(body) ?? "";
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return Truncate(body) ?? "";
        }
    }

    private static string? Truncate(string? text) => text is { Length: > DiagnosticMax } ? text[..DiagnosticMax] : text;
}
