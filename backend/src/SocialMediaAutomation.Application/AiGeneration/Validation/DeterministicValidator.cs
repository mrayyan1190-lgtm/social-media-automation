using System.Globalization;
using System.Text.RegularExpressions;
using SocialMediaAutomation.Application.AiGeneration.Contracts;
using SocialMediaAutomation.Domain.Common;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Application.AiGeneration.Validation;

/// <summary>
/// Rule-based checks that do not depend on the model's judgement (PRD §14). Length, hashtag count and URL syntax are
/// already enforced when the PostVersion is created; these checks add the content rules that produce issues.
/// </summary>
public static partial class DeterministicValidator
{
    public static IReadOnlyList<EvaluationIssue> Validate(AiInputSnapshot snapshot, SocialPlatform platform, PostVersion version, GeneratedVariant variant)
    {
        var issues = new List<EvaluationIssue>();
        var text = string.Join("\n", new[] { version.Hook, version.Body, version.CallToAction }.Where(t => t is not null)
            .Concat(version.Hashtags));

        // Language: the model must have written in the requested language (primary subtag match, e.g. en vs en-GB).
        if (!SameLanguage(variant.Language, snapshot.Language))
            issues.Add(Critical("LANGUAGE", $"Written in '{variant.Language}' but the brand language is '{snapshot.Language}'."));

        foreach (var term in snapshot.Brand.ProhibitedTerms.Where(term => ContainsTerm(text, term)))
            issues.Add(Critical("PROHIBITED_TERM", $"Uses the prohibited term \"{term}\".", term));

        // URLs: only the campaign's approved destination URL may be published; none may be embedded in the text.
        foreach (Match url in UrlPattern().Matches(text))
            issues.Add(Critical("URL", "The text contains a URL; only the approved destination URL may be attached.", url.Value));

        var knownFacts = snapshot.Facts.Select(f => f.Id.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var factId in (variant.FactIdsUsed ?? []).Where(id => !knownFacts.Contains(id.Trim())))
            issues.Add(Critical("FACT_REFERENCE", $"References a fact id that was not supplied: {factId}."));

        // Numbers and money that appear in no supplied fact or brief field are likely invented statistics or prices.
        var evidence = string.Join("\n", snapshot.Facts.Select(f => f.Text)
            .Append(snapshot.Campaign.Description).Append(snapshot.Brand.BusinessDescription));
        foreach (var figure in FigurePattern().Matches(text).Select(m => m.Value.Trim().TrimEnd('.', ',')).Distinct()
                     .Where(f => !evidence.Contains(f, StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add(new EvaluationIssue(IssueSeverity.WARNING, "UNSUPPORTED_CLAIM",
                $"The figure \"{figure}\" does not appear in the supplied facts or brief; verify it or remove it.", IssueSource.DETERMINISTIC, figure));
        }

        if (PlatformRules.For(platform).RequiresImage && version.MediaAssetId is null)
            issues.Add(new EvaluationIssue(IssueSeverity.WARNING, "MEDIA", $"{platform} requires one image before this post can be submitted for review.", IssueSource.DETERMINISTIC));

        if (platform == SocialPlatform.LinkedIn && version.Hashtags.Count > 5)
            issues.Add(new EvaluationIssue(IssueSeverity.INFO, "PLATFORM_FIT", $"{version.Hashtags.Count} hashtags; LinkedIn posts read better with 3-5.", IssueSource.DETERMINISTIC));

        return issues;
    }

    /// <summary>
    /// Critical content rules that hold for every version, including manual edits that never went through the AI
    /// pipeline: no prohibited brand terms and no URLs embedded in the text. Checked again at review submission.
    /// </summary>
    public static IReadOnlyList<EvaluationIssue> SubmissionBlockers(PostVersion version, IReadOnlyList<string> prohibitedTerms)
    {
        var text = string.Join("\n", new[] { version.Hook, version.Body, version.CallToAction }.Where(t => t is not null).Concat(version.Hashtags));
        var issues = prohibitedTerms.Where(term => ContainsTerm(text, term))
            .Select(term => Critical("PROHIBITED_TERM", $"Uses the prohibited term \"{term}\".", term)).ToList();
        issues.AddRange(UrlPattern().Matches(text).Select(url =>
            Critical("URL", "The text contains a URL; only the approved destination URL may be attached.", url.Value)));
        return issues;
    }

    private static bool SameLanguage(string? declared, string expected)
    {
        try
        {
            return declared is not null
                && CultureInfo.GetCultureInfo(declared.Trim()).TwoLetterISOLanguageName == CultureInfo.GetCultureInfo(expected).TwoLetterISOLanguageName;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    private static bool ContainsTerm(string text, string term) =>
        Regex.IsMatch(text, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(term)}(?![\p{{L}}\p{{N}}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static EvaluationIssue Critical(string category, string message, string? excerpt = null) =>
        new(IssueSeverity.CRITICAL, category, message, IssueSource.DETERMINISTIC, excerpt);

    [GeneratedRegex(@"https?://\S+|www\.\S+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    // Percentages, currency amounts, multipliers and grouped large numbers ("50%", "$99", "€1,200", "3x", "10,000").
    [GeneratedRegex(@"[$€£¥]\s?\d[\d,.]*|\d[\d,.]*\s?(%|percent\b|x\b)|\b\d{1,3}(,\d{3})+\b", RegexOptions.IgnoreCase)]
    private static partial Regex FigurePattern();
}
