namespace SocialMediaAutomation.Application.AiGeneration.Services;

/// <summary>
/// The Hugging Face models users may pick in Composer. The first of each list is the default. Users choose only from
/// these lists (never a free model id), so cost and behavior stay within what the platform operator expects.
/// </summary>
public static class AiModelCatalog
{
    public static readonly IReadOnlyList<string> HuggingFaceImageModels =
    [
        "Tongyi-MAI/Z-Image-Turbo",
        "black-forest-labs/FLUX.1-schnell",
        "Efficient-Large-Model/SANA1.5_1.6B_1024px_diffusers",
    ];

    /// <summary>
    /// Fast, non-"thinking" vision models that several Hugging Face providers serve (checked 2026-10-07 on the router's
    /// model list). Reasoning models such as GLM-4.5V can take minutes per answer, which the worker cannot wait for.
    /// </summary>
    public static readonly IReadOnlyList<string> HuggingFaceVisionModels =
    [
        "meta-llama/Llama-4-Scout-17B-16E-Instruct",
        "Qwen/Qwen3-VL-30B-A3B-Instruct",
        "google/gemma-3-27b-it",
    ];

    /// <summary>Per-call limit for vision when the operator set no request timeout: a slow model fails fast instead of hanging.</summary>
    public const int VisionTimeoutSeconds = 60;

    /// <summary>The configured vision model if it is still offered, otherwise the default (models are retired over time).</summary>
    public static string VisionModelOrDefault(string? configured) =>
        configured is not null && HuggingFaceVisionModels.Contains(configured) ? configured : HuggingFaceVisionModels[0];

    /// <summary>The list users may pick from, with the operator's configured model first (it is the default).</summary>
    public static IReadOnlyList<string> WithDefault(IReadOnlyList<string> models, string? preferred) =>
        string.IsNullOrWhiteSpace(preferred) ? models : [preferred, .. models.Where(m => m != preferred)];

    /// <summary>Uploaded images analysed per generation (the first is also attached to the post).</summary>
    public const int MaxVisionImages = 4;

    /// <summary>
    /// Images travel inline (base64) in one chat request, so their total size is capped to stay within provider limits.
    /// ponytail: no server-side downscaling; add it if users hit this limit with phone photos.
    /// </summary>
    public const long MaxVisionBytes = 12 * 1024 * 1024;
}
