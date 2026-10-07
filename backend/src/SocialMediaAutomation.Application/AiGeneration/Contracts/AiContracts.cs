using System.Text.Json;
using System.Text.Json.Serialization;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Application.AiGeneration.Contracts;

/// <summary>Versioned identifiers recorded on every request so outputs can be traced to the exact prompt and schemas.</summary>
public static class AiVersions
{
    public const string Snapshot = "snapshot-v3";
    public const string ContentSchema = "content-v2";
    public const string EvaluationSchema = "evaluation-v2";
    public const string GenerationPrompt = "generation-v4";
    public const string RewritePrompt = "rewrite-v2";
    public const string EvaluationPrompt = "evaluation-v2";
    public const string ImagePrompt = "image-v2";
    /// <summary>An image prompt written by the LLM during generation, wrapped with the fixed safety rules.</summary>
    public const string GeneratedImagePrompt = "image-llm-v1";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

// ---- Frozen input snapshot (stored on AiGenerationRequest.InputSnapshot) --------------------------------------------

/// <summary>Everything the model may use. Brand website is deliberately excluded: it is metadata, not a factual source.</summary>
public sealed record AiInputSnapshot(
    string SchemaVersion,
    Guid OrganizationId,
    string Language,
    BrandSnapshot Brand,
    BriefSnapshot Brief,
    IReadOnlyList<SocialPlatform> Platforms,
    RewriteSnapshot? Rewrite,
    ImageSnapshot? Image = null,
    VisionSnapshot? Vision = null,
    ImagePlanSnapshot? ImagePlan = null,
    bool? Unified = null);

// Unified (AI Studio "All platforms"): one post with the same text on every platform, so it must fit the strictest
// platform. Null (not false) when off, so existing snapshots and duplicate-click matching stay unchanged.

/// <summary>Business name, industry and language come from the Organization; the rest from the brand profile.</summary>
public sealed record BrandSnapshot(
    string BusinessName, string Industry, string BusinessDescription, string TargetAudience, BrandTone BrandTone,
    IReadOnlyList<string> ProhibitedTerms, DateTime BrandProfileUpdatedAtUtc);

/// <summary>
/// What the user asked for in AI Studio (Prompt is null for rewrites and images of an existing post), with the brand
/// audience and the resolved tone (an optional override, else the brand tone) and writing options.
/// </summary>
public sealed record BriefSnapshot(
    string? Prompt, string TargetAudience, BrandTone Tone, ContentLength ContentLength, HashtagMode HashtagMode, EmojiMode EmojiMode);

public sealed record RewriteSnapshot(SocialPlatform Platform, RewriteAction Action, string? CustomInstruction, SourceContentSnapshot SourceVersion);

public sealed record SourceContentSnapshot(int VersionNumber, string? Hook, string Body, string? CallToAction, IReadOnlyList<string> Hashtags, bool HasImage);

/// <summary>An image request for one post: the post text it should illustrate and the user's optional visual direction.</summary>
public sealed record ImageSnapshot(SocialPlatform Platform, string? Instruction, SourceContentSnapshot SourceVersion, string Size);

/// <summary>
/// Uploaded images a vision model reads to write the post (ids only; the worker loads the bytes). With Attach (Text + Image)
/// the first one becomes the drafts' image; with Text only it is analysed but not attached.
/// </summary>
public sealed record VisionSnapshot(IReadOnlyList<Guid> MediaAssetIds, bool Attach = true);

/// <summary>
/// The LLM also writes an image prompt, which this image model renders. Text + Image always plans an image; with
/// OnlyIfRequested (Text, or a Composer edit) the LLM writes a prompt only when the user's own words ask for an image.
/// </summary>
public sealed record ImagePlanSnapshot(AiProvider Provider, string Model, string Size, bool OnlyIfRequested = false);

// ---- Model output (parsed leniently, then validated strictly) ----------------------------------------------------------

public sealed class GeneratedContent
{
    public string? SchemaVersion { get; set; }
    public List<GeneratedVariant>? Variants { get; set; }
    /// <summary>Only when an image is planned: the LLM-written prompt for one image that suits every variant.</summary>
    public string? ImagePrompt { get; set; }
}

public sealed class GeneratedVariant
{
    public string? Platform { get; set; }
    public string? Language { get; set; }
    public string? Hook { get; set; }
    public string? Body { get; set; }
    public string? Cta { get; set; }
    public List<string>? Hashtags { get; set; }
}

public sealed class EvaluationContent
{
    public List<VariantEvaluation>? Evaluations { get; set; }
}

public sealed class VariantEvaluation
{
    public string? Platform { get; set; }
    public int? BrandAlignment { get; set; }
    public int? AudienceRelevance { get; set; }
    public int? Clarity { get; set; }
    public int? PlatformFit { get; set; }
    public int? CtaRelevance { get; set; }
    public string? Summary { get; set; }
    public List<ClaimCheck>? Claims { get; set; }
    public List<AiFinding>? Issues { get; set; }
}

public sealed class ClaimCheck
{
    public string? Text { get; set; }
    public string? Kind { get; set; }
    public bool Supported { get; set; }
    public bool ContradictsFacts { get; set; }
}

public sealed class AiFinding
{
    public string? Severity { get; set; }
    public string? Category { get; set; }
    public string? Message { get; set; }
    public string? Excerpt { get; set; }
}
