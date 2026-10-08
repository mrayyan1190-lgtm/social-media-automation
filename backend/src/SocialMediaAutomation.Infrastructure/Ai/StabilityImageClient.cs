using System.Diagnostics;
using System.Net.Http.Headers;
using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Domain.Entities;

namespace SocialMediaAutomation.Infrastructure.Ai;

/// <summary>
/// Stability AI Stable Image (POST /v2beta/stable-image/generate/{core|ultra|sd3}, multipart/form-data). The model name is
/// the endpoint; "core" is Stable Image Core. JPEG is requested because Instagram accepts only JPEG.
/// </summary>
internal sealed class StabilityImageClient(IHttpClientFactory httpClientFactory) : IImageGenerationProvider
{
    public async Task<GeneratedImage> GenerateAsync(ImageConnection connection, string prompt, CancellationToken cancellationToken)
    {
        var model = connection.Model.Trim().ToLowerInvariant();
        if (!PlatformAiConfiguration.StabilityImageModels.Contains(model))
            throw new LlmException(LlmErrorKind.Rejected, $"Stability AI does not offer \"{connection.Model}\"; use one of {string.Join(", ", PlatformAiConfiguration.StabilityImageModels)}.", null, 0, null);

        var form = new MultipartFormDataContent
        {
            { new StringContent(prompt), "prompt" },
            { new StringContent(AspectRatio(connection.Size)), "aspect_ratio" },
            { new StringContent("jpeg"), "output_format" },
        };
        using var message = new HttpRequestMessage(HttpMethod.Post, $"{connection.BaseUrl}/v2beta/stable-image/generate/{model}") { Content = form };
        message.Headers.Accept.ParseAdd("image/*");
        var (status, bytes, stopwatch) = await SendAsync(message, connection.ApiKey, cancellationToken);
        var latency = (int)stopwatch.ElapsedMilliseconds;
        if (status is < 200 or >= 300)
            OpenAiImageClient.ThrowIfFailed(status, System.Text.Encoding.UTF8.GetString(bytes), latency);
        if (bytes.Length == 0)
            throw new LlmException(LlmErrorKind.Transient, "The image provider returned no image.", status, latency, null);
        return new GeneratedImage(bytes, latency, status);
    }

    /// <summary>Checks the key against the account endpoint, which generates nothing and costs no credits.</summary>
    public async Task<bool> IsModelAvailableAsync(ImageConnection connection, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, $"{connection.BaseUrl}/v1/user/account");
        var (status, bytes, stopwatch) = await SendAsync(message, connection.ApiKey, cancellationToken);
        OpenAiImageClient.ThrowIfFailed(status, System.Text.Encoding.UTF8.GetString(bytes), (int)stopwatch.ElapsedMilliseconds);
        return string.IsNullOrWhiteSpace(connection.Model) || PlatformAiConfiguration.StabilityImageModels.Contains(connection.Model.Trim().ToLowerInvariant());
    }

    /// <summary>The platform's image sizes mapped to Stability's aspect ratios (output is about 1.5 megapixels).</summary>
    private static string AspectRatio(string size) => size switch
    {
        "1536x1024" => "3:2",
        "1792x1024" => "16:9",
        _ => "1:1",
    };

    private async Task<(int Status, byte[] Body, Stopwatch Stopwatch)> SendAsync(HttpRequestMessage message, string apiKey, CancellationToken cancellationToken)
    {
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await httpClientFactory.CreateClient(OpenAiImageClient.HttpClientName).SendAsync(message, cancellationToken);
            return ((int)response.StatusCode, await response.Content.ReadAsByteArrayAsync(cancellationToken), stopwatch);
        }
        catch (HttpRequestException ex)
        {
            throw new LlmException(LlmErrorKind.Transient, $"Stability AI could not be reached ({ex.HttpRequestError}).", null, (int)stopwatch.ElapsedMilliseconds, null);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LlmException(LlmErrorKind.Transient, "Stability AI did not respond in time.", null, (int)stopwatch.ElapsedMilliseconds, null);
        }
    }
}
