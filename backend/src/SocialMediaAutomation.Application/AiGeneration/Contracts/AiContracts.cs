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
    ImageSnapshot? Image = null);

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

// ---- Model output (parsed leniently, then validated strictly) ----------------------------------------------------------

public sealed class GeneratedContent
{
    public string? SchemaVersion { get; set; }
    public List<GeneratedVariant>? Variants { get; set; }
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
