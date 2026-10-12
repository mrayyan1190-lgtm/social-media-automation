using System.Text.Json;
using SocialMediaAutomation.Application.AiGeneration.Contracts;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Application.AiGeneration.Validation;

/// <summary>Model output that does not match the required schema. Never persisted as content.</summary>
public sealed class AiOutputInvalidException(string message, string? rawOutput) : Exception(message)
{
    public string? RawOutput { get; } = rawOutput;
}

/// <summary>Strict structural validation of model JSON (schema, platform keys, required fields).</summary>
public static class OutputParser
{
    public static IReadOnlyDictionary<SocialPlatform, GeneratedVariant> ParseContent(string raw, IReadOnlyCollection<SocialPlatform> expected)
    {
        var content = Deserialize<GeneratedContent>(raw);
        if (content.SchemaVersion is not null && content.SchemaVersion != AiVersions.ContentSchema)
            throw new AiOutputInvalidException($"Unexpected schemaVersion '{content.SchemaVersion}'.", raw);
        if (content.Variants is not { Count: > 0 })
            throw new AiOutputInvalidException("The response has no variants.", raw);

        var result = new Dictionary<SocialPlatform, GeneratedVariant>();
        foreach (var variant in content.Variants)
        {
            if (!Enum.TryParse<SocialPlatform>(variant.Platform, ignoreCase: true, out var platform) || !Enum.IsDefined(platform))
                throw new AiOutputInvalidException($"Unknown platform '{variant.Platform}'.", raw);
            if (!expected.Contains(platform))
                throw new AiOutputInvalidException($"Platform {platform} was not requested.", raw);
            if (!result.TryAdd(platform, variant))
                throw new AiOutputInvalidException($"Platform {platform} appears more than once.", raw);
            if (string.IsNullOrWhiteSpace(variant.Body))
                throw new AiOutputInvalidException($"The {platform} variant has no body.", raw);
            if (string.IsNullOrWhiteSpace(variant.Language))
                throw new AiOutputInvalidException($"The {platform} variant does not declare its language.", raw);
        }

        var missing = expected.Where(p => !result.ContainsKey(p)).ToList();
        if (missing.Count > 0)
            throw new AiOutputInvalidException($"Missing variants for: {string.Join(", ", missing)}.", raw);
        return result;
    }

    /// <summary>The optional LLM-written image prompt (null when missing), clipped to a sane length.</summary>
    public static string? ParseImagePrompt(string raw)
    {
        var prompt = Deserialize<GeneratedContent>(raw).ImagePrompt?.Trim();
        return string.IsNullOrEmpty(prompt) ? null : prompt.Length > 1500 ? prompt[..1500] : prompt;
    }

    /// <summary>How many images the user asked for, per the model (1 when missing), clamped to 1..MaxImages.</summary>
    /// <summary>A rewrite's imageMode: "add" keeps the current images and appends the new ones; anything else replaces them.</summary>
    public static bool ParseAddsImages(string raw) =>
        string.Equals(Deserialize<GeneratedContent>(raw).ImageMode?.Trim(), "add", StringComparison.OrdinalIgnoreCase);

    public static int ParseImageCount(string raw) =>
        Math.Clamp(Deserialize<GeneratedContent>(raw).ImageCount ?? 1, 1, Domain.Common.PlatformRules.MaxImages);

    public static IReadOnlyDictionary<SocialPlatform, VariantEvaluation> ParseEvaluation(string raw)
    {
        var content = Deserialize<EvaluationContent>(raw);
        var result = new Dictionary<SocialPlatform, VariantEvaluation>();
        foreach (var evaluation in content.Evaluations ?? [])
        {
            if (Enum.TryParse<SocialPlatform>(evaluation.Platform, ignoreCase: true, out var platform) && Enum.IsDefined(platform))
                result.TryAdd(platform, evaluation);
        }
        return result;
    }

    private static T Deserialize<T>(string raw)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(StripCodeFence(raw), AiVersions.Json)
                ?? throw new AiOutputInvalidException("The response is empty.", raw);
        }
        catch (JsonException ex)
        {
            throw new AiOutputInvalidException($"The response is not valid JSON for the expected schema: {ex.Message}", raw);
        }
    }

    // Some OpenAI-compatible providers wrap JSON in a markdown fence even in JSON mode.
    private static string StripCodeFence(string raw)
    {
        var text = raw.Trim();
        if (!text.StartsWith("```", StringComparison.Ordinal))
            return text;
        var firstNewline = text.IndexOf('\n');
        var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
        return firstNewline > 0 && lastFence > firstNewline ? text[(firstNewline + 1)..lastFence].Trim() : text;
    }
}
