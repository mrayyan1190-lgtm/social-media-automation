using System.Text.RegularExpressions;

namespace SocialMediaAutomation.Domain.Common;

/// <summary>
/// One id per operation (API request, AI request, publication job) so it can be traced end to end. Only safe characters
/// are kept: correlation ids appear in logs and admin screens.
/// </summary>
public static partial class CorrelationIds
{
    public const int MaxLength = 64;

    public static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxLength || !Safe().IsMatch(trimmed) ? null : trimmed;
    }

    /// <summary>A short readable id, e.g. COR-84F9C2A1, for work that did not come from an API request.</summary>
    public static string New() => $"COR-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";

    [GeneratedRegex("^[A-Za-z0-9._:-]+$")]
    private static partial Regex Safe();
}
