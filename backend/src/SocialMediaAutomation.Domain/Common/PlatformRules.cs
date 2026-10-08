using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Domain.Common;

/// <summary>
/// Deterministic per-platform content and media rules for the MVP targets (PRD §24). Conservative values:
/// a draft that passes here should not be rejected by the provider for size, length or format.
/// </summary>
public sealed record PlatformRules(
    SocialPlatform Platform,
    int MaxTextLength,
    int MaxHashtags,
    bool RequiresImage,
    IReadOnlySet<string> AllowedImageTypes,
    double MinAspectRatio,
    double MaxAspectRatio)
{
    public const string Jpeg = "image/jpeg";
    public const string Png = "image/png";
    public const long MaxImageBytes = 8 * 1024 * 1024;
    public const int MinImageSide = 200;
    public const int MaxImageSide = 8192;
    /// <summary>Images per version: one image post, or a 2-4 image LinkedIn multi-image / Facebook multi-photo / Instagram carousel post.</summary>
    public const int MaxImages = 4;

    private static readonly Dictionary<SocialPlatform, PlatformRules> Rules = new()
    {
        [SocialPlatform.LinkedIn] = new(SocialPlatform.LinkedIn, 3000, 30, false, new HashSet<string> { Jpeg, Png }, 1 / 2.4, 2.4),
        [SocialPlatform.Facebook] = new(SocialPlatform.Facebook, 5000, 30, false, new HashSet<string> { Jpeg, Png }, 1 / 3.0, 3.0),
        // Instagram feed posts (single image or carousel) need at least one JPEG, each between 4:5 portrait and 1.91:1 landscape, and at most 30 hashtags.
        [SocialPlatform.Instagram] = new(SocialPlatform.Instagram, 2200, 30, true, new HashSet<string> { Jpeg }, 4 / 5.0, 1.91),
    };

    public static PlatformRules For(SocialPlatform platform) => Rules[platform];
}
