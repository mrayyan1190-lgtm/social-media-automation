using System.Text;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Application.Abstractions;

/// <summary>Where and how to call the image model. ToString is redacted so the key cannot reach a log.</summary>
public sealed record ImageConnection(AiProvider Provider, string BaseUrl, string Model, string ApiKey, string Size)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"Provider = {Provider}, BaseUrl = {BaseUrl}, Model = {Model}, Size = {Size}, ApiKey = ***");
        return true;
    }
}

/// <summary>The image bytes as returned by the provider (validated by the caller from the bytes themselves).</summary>
public sealed record GeneratedImage(byte[] Bytes, int LatencyMs, int HttpStatus);

/// <summary>
/// Vendor-neutral image generation (AGENTS.md §31). Failures are normalized to <see cref="LlmException"/> kinds
/// (Authentication, Rejected, Transient) so the AI worker's retry policy applies unchanged.
/// </summary>
public interface IImageGenerationProvider
{
    Task<GeneratedImage> GenerateAsync(ImageConnection connection, string prompt, CancellationToken cancellationToken);
}
