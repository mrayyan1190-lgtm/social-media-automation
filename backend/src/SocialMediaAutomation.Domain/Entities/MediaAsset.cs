using SocialMediaAutomation.Domain.Common;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Domain.Entities;

/// <summary>
/// Immutable metadata for one stored image. Identity is (Id, ObjectKey, Sha256); the bytes live in object storage
/// under ObjectKey and are never overwritten. Delivery URLs are generated when needed and are never stored here.
/// </summary>
public sealed class MediaAsset
{
    public const int FileNameMax = 255;

    private MediaAsset() { }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string OriginalFileName { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public long SizeBytes { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public string Sha256 { get; private set; } = null!;
    public string ObjectKey { get; private set; } = null!;
    public MediaSourceType SourceType { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>For AI_GENERATED images: the durable request, provider and model that produced it (null for uploads).</summary>
    public Guid? GenerationRequestId { get; private set; }
    public string? GenerationProvider { get; private set; }
    public string? GenerationModel { get; private set; }

    public double AspectRatio => (double)Width / Height;

    /// <summary>
    /// An image produced by an image model. Same validation and immutability as an upload; it reaches a post only as a new
    /// PostVersion, so it always goes through human review like any other change.
    /// </summary>
    public static MediaAsset CreateGenerated(
        Guid id, Guid organizationId, string contentType, long sizeBytes, int width, int height, string sha256, string objectKey,
        Guid generationRequestId, string provider, string model, Guid userId, DateTime utcNow)
    {
        var asset = CreateUpload(id, organizationId, $"ai-image-{id.ToString("N")[..8]}", contentType, sizeBytes, width, height, sha256, objectKey, userId, utcNow);
        asset.SourceType = MediaSourceType.AiGenerated;
        asset.GenerationRequestId = generationRequestId;
        asset.GenerationProvider = Guard.Required(provider, "Provider", 50);
        asset.GenerationModel = Guard.Required(model, "Model", AiProviderSettings.ModelMax);
        return asset;
    }

    /// <param name="contentType">The type detected from the file's bytes, never the client-declared type.</param>
    public static MediaAsset CreateUpload(
        Guid id, Guid organizationId, string? originalFileName, string contentType, long sizeBytes, int width, int height,
        string sha256, string objectKey, Guid userId, DateTime utcNow)
    {
        if (contentType is not (PlatformRules.Jpeg or PlatformRules.Png))
            throw Guard.Invalid("Image", "must be a JPEG or PNG file");
        if (sizeBytes is <= 0 or > PlatformRules.MaxImageBytes)
            throw Guard.Invalid("Image", $"must be at most {PlatformRules.MaxImageBytes / (1024 * 1024)} MB");
        if (width < PlatformRules.MinImageSide || height < PlatformRules.MinImageSide || width > PlatformRules.MaxImageSide || height > PlatformRules.MaxImageSide)
            throw Guard.Invalid("Image", $"dimensions must be between {PlatformRules.MinImageSide} and {PlatformRules.MaxImageSide} pixels per side");

        var name = Path.GetFileName(originalFileName?.Trim() ?? "");
        return new MediaAsset
        {
            Id = id,
            OrganizationId = organizationId,
            OriginalFileName = name.Length == 0 ? "upload" : name[..Math.Min(name.Length, FileNameMax)],
            ContentType = contentType,
            SizeBytes = sizeBytes,
            Width = width,
            Height = height,
            Sha256 = Guard.Required(sha256, "SHA-256", 64).ToLowerInvariant(),
            ObjectKey = Guard.Required(objectKey, "Object key", 300),
            SourceType = MediaSourceType.Upload,
            CreatedByUserId = userId,
            CreatedAtUtc = utcNow,
        };
    }

    /// <summary>Platform-specific acceptance (type and aspect ratio), checked whenever the image is attached to a post version.</summary>
    public void EnsureSuitableFor(SocialPlatform platform)
    {
        var rules = PlatformRules.For(platform);
        if (!rules.AllowedImageTypes.Contains(ContentType))
            throw Guard.Invalid("Image", $"type {ContentType} is not accepted by {platform} (allowed: {string.Join(", ", rules.AllowedImageTypes)})");
        if (AspectRatio < rules.MinAspectRatio - 0.01 || AspectRatio > rules.MaxAspectRatio + 0.01)
            throw Guard.Invalid("Image", $"aspect ratio {Width}x{Height} is outside what {platform} accepts");
    }
}
