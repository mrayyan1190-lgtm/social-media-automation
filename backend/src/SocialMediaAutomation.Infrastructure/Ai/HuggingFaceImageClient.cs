using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Infrastructure.Ai;

/// <summary>
/// Hugging Face Inference Providers text-to-image, e.g. Tongyi-MAI/Z-Image-Turbo (default), black-forest-labs/FLUX.1-schnell
/// or Efficient-Large-Model/SANA1.5_1.6B_1024px_diffusers. The Hub maps the model id to the serving provider's id; the call
/// goes through the HF router with the HF token. JPEG is requested because Instagram accepts only JPEG.
/// ponytail: fal-ai route only (it serves all three suggested models); add other HF providers when a chosen model needs one.
/// </summary>
internal sealed partial class HuggingFaceImageClient(IHttpClientFactory httpClientFactory) : IImageGenerationProvider
{
    public const string HubApi = "https://huggingface.co/api";
    private const string RouteProvider = "fal-ai";

    public async Task<GeneratedImage> GenerateAsync(ImageConnection connection, string prompt, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var providerModel = await ProviderModelAsync(connection, cancellationToken)
            ?? throw new LlmException(LlmErrorKind.Rejected, $"Hugging Face does not serve \"{connection.Model}\" for text-to-image.", null,
                (int)stopwatch.ElapsedMilliseconds, null);
        var (width, height) = ParseSize(connection.Size);
        var body = new JsonObject
        {
            ["prompt"] = prompt,
            ["image_size"] = new JsonObject { ["width"] = width, ["height"] = height },
            ["num_images"] = 1,
            ["output_format"] = "jpeg",
            ["sync_mode"] = true, // the image comes back inline as a data URI
        };
        var router = new Uri(connection.BaseUrl).GetLeftPart(UriPartial.Authority);
        var (status, json) = await SendAsync(HttpMethod.Post, $"{router}/{RouteProvider}/{providerModel}", connection.ApiKey, body, stopwatch, cancellationToken);
        var latency = (int)stopwatch.ElapsedMilliseconds;
        OpenAiImageClient.ThrowIfFailed(status, json, latency);

        string url;
        try
        {
            url = JsonNode.Parse(json)?["images"]?[0]?["url"]?.GetValue<string>()
                ?? throw new LlmException(LlmErrorKind.Transient, "The image provider returned no image.", status, latency, null);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw new LlmException(LlmErrorKind.Transient, "The image provider response was not valid.", status, latency, null);
        }

        byte[] bytes;
        if (url.StartsWith("data:", StringComparison.Ordinal))
        {
            try
            {
                bytes = Convert.FromBase64String(url[(url.IndexOf(',') + 1)..]);
            }
            catch (FormatException)
            {
                throw new LlmException(LlmErrorKind.Transient, "The image provider response was not valid.", status, latency, null);
            }
        }
        else
        {
            bytes = await DownloadAsync(url, latency, cancellationToken);
        }
        return new GeneratedImage(bytes, (int)stopwatch.ElapsedMilliseconds, status);
    }

    public async Task<bool> IsModelAvailableAsync(ImageConnection connection, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var (status, json) = await SendAsync(HttpMethod.Get, $"{HubApi}/whoami-v2", connection.ApiKey, null, stopwatch, cancellationToken);
        OpenAiImageClient.ThrowIfFailed(status, json, (int)stopwatch.ElapsedMilliseconds);
        return string.IsNullOrWhiteSpace(connection.Model) || await ProviderModelAsync(connection, cancellationToken) is not null;
    }

    /// <summary>The provider's id for the model when it is live for text-to-image on the route we call; otherwise null.</summary>
    private async Task<string?> ProviderModelAsync(ImageConnection connection, CancellationToken cancellationToken)
    {
        var model = connection.Model.Trim();
        if (!ModelId().IsMatch(model))
            return null;
        var stopwatch = Stopwatch.StartNew();
        var (status, json) = await SendAsync(HttpMethod.Get, $"{HubApi}/models/{model}?expand[]=inferenceProviderMapping", connection.ApiKey, null,
            stopwatch, cancellationToken);
        if (status == 404)
            return null;
        OpenAiImageClient.ThrowIfFailed(status, json, (int)stopwatch.ElapsedMilliseconds);
        try
        {
            var mapping = JsonNode.Parse(json)?["inferenceProviderMapping"]?[RouteProvider];
            return mapping?["status"]?.GetValue<string>() == "live" && mapping["task"]?.GetValue<string>() == "text-to-image"
                ? mapping["providerId"]?.GetValue<string>()
                : null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>A hosted result URL is fetched without the HF token: the token is only ever sent to Hugging Face.</summary>
    private async Task<byte[]> DownloadAsync(string url, int latency, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new LlmException(LlmErrorKind.Transient, "The image provider returned an unusable image link.", null, latency, null);
        try
        {
            return await httpClientFactory.CreateClient(OpenAiImageClient.HttpClientName).GetByteArrayAsync(uri, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new LlmException(LlmErrorKind.Transient, $"The generated image could not be downloaded ({ex.StatusCode}).", null, latency, null);
        }
    }

    private async Task<(int Status, string Body)> SendAsync(
        HttpMethod method, string url, string apiKey, JsonObject? body, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(method, url);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        if (body is not null)
            message.Content = JsonContent.Create(body);
        try
        {
            using var response = await httpClientFactory.CreateClient(OpenAiImageClient.HttpClientName).SendAsync(message, cancellationToken);
            return ((int)response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (HttpRequestException ex)
        {
            throw new LlmException(LlmErrorKind.Transient, $"Hugging Face could not be reached ({ex.HttpRequestError}).", null, (int)stopwatch.ElapsedMilliseconds, null);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LlmException(LlmErrorKind.Transient, "Hugging Face did not respond in time.", null, (int)stopwatch.ElapsedMilliseconds, null);
        }
    }

    private static (int Width, int Height) ParseSize(string size) =>
        size.Split('x') is [var w, var h] && int.TryParse(w, out var width) && int.TryParse(h, out var height) ? (width, height) : (1024, 1024);

    /// <summary>owner/name only, so the configured id cannot alter the request path.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9][\w.-]*/[\w.-]+$")]
    private static partial Regex ModelId();
}

/// <summary>Picks the image adapter for the configured provider; provider specifics stay inside the adapters (AGENTS.md §45).</summary>
internal sealed class ImageGenerationRouter(OpenAiImageClient openAi, HuggingFaceImageClient huggingFace, StabilityImageClient stability,
    CloudflareImageClient cloudflare) : IImageGenerationProvider
{
    public Task<GeneratedImage> GenerateAsync(ImageConnection connection, string prompt, CancellationToken cancellationToken) =>
        For(connection).GenerateAsync(connection, prompt, cancellationToken);

    public Task<bool> IsModelAvailableAsync(ImageConnection connection, CancellationToken cancellationToken) =>
        For(connection).IsModelAvailableAsync(connection, cancellationToken);

    private IImageGenerationProvider For(ImageConnection connection) => connection.Provider switch
    {
        AiProvider.HuggingFace => huggingFace,
        AiProvider.StabilityAI => stability,
        AiProvider.CloudflareWorkersAI => cloudflare,
        _ => openAi,
    };
}
