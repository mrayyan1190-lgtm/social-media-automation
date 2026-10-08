using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Domain.Entities;

namespace SocialMediaAutomation.Infrastructure.Ai;

/// <summary>
/// Cloudflare Workers AI text-to-image (POST {account}/ai/run/{model}). The base URL already carries the account id; the
/// API token is the image key. FLUX.1 [schnell] returns a base64 JPEG (1024x1024), which every platform accepts.
/// </summary>
internal sealed class CloudflareImageClient(IHttpClientFactory httpClientFactory) : IImageGenerationProvider
{
    /// <summary>FLUX.1 [schnell] allows at most 8 diffusion steps; the maximum gives the best quality.</summary>
    private const int Steps = 8;

    public async Task<GeneratedImage> GenerateAsync(ImageConnection connection, string prompt, CancellationToken cancellationToken)
    {
        var model = Model(connection);
        var body = new JsonObject { ["prompt"] = prompt, ["steps"] = Steps };
        var (status, json, stopwatch) = await SendAsync(HttpMethod.Post, $"{connection.BaseUrl}/run/{model}", connection.ApiKey, body, cancellationToken);
        var latency = (int)stopwatch.ElapsedMilliseconds;
        OpenAiImageClient.ThrowIfFailed(status, json, latency);
        try
        {
            var b64 = JsonNode.Parse(json)?["result"]?["image"]?.GetValue<string>()
                ?? throw new LlmException(LlmErrorKind.Transient, "The image provider returned no image.", status, latency, null);
            return new GeneratedImage(Convert.FromBase64String(b64), latency, status);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            throw new LlmException(LlmErrorKind.Transient, "The image provider response was not valid.", status, latency, null);
        }
    }

    /// <summary>Searches the account's model catalog: checks the account id and token together, generates nothing.</summary>
    public async Task<bool> IsModelAvailableAsync(ImageConnection connection, CancellationToken cancellationToken)
    {
        var (status, json, stopwatch) = await SendAsync(HttpMethod.Get, $"{connection.BaseUrl}/models/search?search=flux-1-schnell",
            connection.ApiKey, null, cancellationToken);
        // A wrong account id answers 403/404; both mean these credentials cannot run the model.
        OpenAiImageClient.ThrowIfFailed(status is 404 ? 403 : status, json, (int)stopwatch.ElapsedMilliseconds);
        if (string.IsNullOrWhiteSpace(connection.Model))
            return true;
        if (!PlatformAiConfiguration.CloudflareImageModels.Contains(connection.Model.Trim()))
            return false;
        try
        {
            return JsonNode.Parse(json)?["result"]?.AsArray().Any(m => m?["name"]?.GetValue<string>() == connection.Model.Trim()) == true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    private static string Model(ImageConnection connection) =>
        PlatformAiConfiguration.CloudflareImageModels.Contains(connection.Model.Trim())
            ? connection.Model.Trim()
            : throw new LlmException(LlmErrorKind.Rejected,
                $"Cloudflare Workers AI is configured for {string.Join(", ", PlatformAiConfiguration.CloudflareImageModels)} only.", null, 0, null);

    private async Task<(int Status, string Body, Stopwatch Stopwatch)> SendAsync(
        HttpMethod method, string url, string apiToken, JsonObject? body, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(method, url);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
        if (body is not null)
            message.Content = JsonContent.Create(body);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await httpClientFactory.CreateClient(OpenAiImageClient.HttpClientName).SendAsync(message, cancellationToken);
            return ((int)response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken), stopwatch);
        }
        catch (HttpRequestException ex)
        {
            throw new LlmException(LlmErrorKind.Transient, $"Cloudflare Workers AI could not be reached ({ex.HttpRequestError}).", null, (int)stopwatch.ElapsedMilliseconds, null);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LlmException(LlmErrorKind.Transient, "Cloudflare Workers AI did not respond in time.", null, (int)stopwatch.ElapsedMilliseconds, null);
        }
    }
}
