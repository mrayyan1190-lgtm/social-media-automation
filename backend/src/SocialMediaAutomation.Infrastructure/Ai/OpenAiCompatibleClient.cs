using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using SocialMediaAutomation.Application.Abstractions;

namespace SocialMediaAutomation.Infrastructure.Ai;

/// <summary>
/// Chat-completions client for OpenAI, Groq and other OpenAI-compatible endpoints. Requests JSON-object responses;
/// the application validates the JSON against its own schema. Neither the key nor prompt bodies are ever logged.
/// </summary>
internal sealed class OpenAiCompatibleClient(IHttpClientFactory httpClientFactory) : ILlmClient
{
    public const string HttpClientName = "llm";
    private const int DiagnosticMax = 2000;

    public async Task<LlmChatResponse> CompleteJsonAsync(LlmConnection connection, LlmChatRequest request, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["model"] = connection.Model,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = request.SystemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = request.UserContent }),
            ["temperature"] = request.Temperature,
            ["max_completion_tokens"] = request.MaxOutputTokens,
            ["response_format"] = new JsonObject { ["type"] = "json_object" },
        };

        var stopwatch = Stopwatch.StartNew();
        var (status, json) = await SendAsync(connection, HttpMethod.Post, "chat/completions", body, stopwatch, cancellationToken);
        // Some reasoning models accept only their default temperature; retry once without it rather than fail.
        if (status == HttpStatusCode.BadRequest && json.Contains("temperature", StringComparison.OrdinalIgnoreCase))
        {
            body.Remove("temperature");
            (status, json) = await SendAsync(connection, HttpMethod.Post, "chat/completions", body, stopwatch, cancellationToken);
        }
        ThrowIfFailed(status, json, (int)stopwatch.ElapsedMilliseconds);

        try
        {
            var root = JsonNode.Parse(json)!;
            var content = root["choices"]?[0]?["message"]?["content"]?.GetValue<string>()
                ?? throw new LlmException(LlmErrorKind.Transient, "The provider returned no message content.", (int)status, (int)stopwatch.ElapsedMilliseconds, Truncate(json));
            return new LlmChatResponse(
                content,
                root["model"]?.GetValue<string>(),
                root["usage"]?["prompt_tokens"]?.GetValue<int>(),
                root["usage"]?["completion_tokens"]?.GetValue<int>(),
                (int)stopwatch.ElapsedMilliseconds,
                (int)status);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            throw new LlmException(LlmErrorKind.Transient, "The provider response was not valid JSON.", (int)status, (int)stopwatch.ElapsedMilliseconds, Truncate(json));
        }
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(LlmConnection connection, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var (status, json) = await SendAsync(connection, HttpMethod.Get, "models", null, stopwatch, cancellationToken);
        ThrowIfFailed(status, json, (int)stopwatch.ElapsedMilliseconds);
        try
        {
            return JsonNode.Parse(json)?["data"]?.AsArray()
                .Select(m => m?["id"]?.GetValue<string>())
                .OfType<string>()
                .Distinct()
                .ToList() ?? [];
        }
        catch (JsonException)
        {
            throw new LlmException(LlmErrorKind.Rejected, "The provider's model list was not valid JSON.", (int)status, (int)stopwatch.ElapsedMilliseconds, Truncate(json));
        }
    }

    private async Task<(HttpStatusCode Status, string Body)> SendAsync(
        LlmConnection connection, HttpMethod method, string path, JsonObject? body, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(method, $"{connection.BaseUrl.TrimEnd('/')}/{path}");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.ApiKey);
        if (body is not null)
            message.Content = JsonContent.Create(body);
        try
        {
            using var response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(message, cancellationToken);
            return (response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (HttpRequestException ex)
        {
            throw new LlmException(LlmErrorKind.Transient, $"The provider could not be reached ({ex.HttpRequestError}).", null, (int)stopwatch.ElapsedMilliseconds, null);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LlmException(LlmErrorKind.Transient, "The provider did not respond in time.", null, (int)stopwatch.ElapsedMilliseconds, null);
        }
    }

    private static void ThrowIfFailed(HttpStatusCode status, string body, int latencyMs)
    {
        var code = (int)status;
        if (code is >= 200 and < 300)
            return;
        var (kind, summary) = code switch
        {
            401 or 403 => (LlmErrorKind.Authentication, "The provider rejected the API key."),
            408 or 409 or 429 or >= 500 => (LlmErrorKind.Transient, $"The provider is temporarily unavailable (HTTP {code})."),
            _ => (LlmErrorKind.Rejected, $"The provider rejected the request (HTTP {code}): {ProviderMessage(body)}"),
        };
        throw new LlmException(kind, summary, code, latencyMs, Truncate(body));
    }

    private static string ProviderMessage(string body)
    {
        try
        {
            return JsonNode.Parse(body)?["error"]?["message"]?.GetValue<string>() ?? Truncate(body) ?? "";
        }
        catch (JsonException)
        {
            return Truncate(body) ?? "";
        }
    }

    private static string? Truncate(string? text) => text is { Length: > DiagnosticMax } ? text[..DiagnosticMax] : text;
}
