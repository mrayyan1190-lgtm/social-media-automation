using System.Globalization;
using System.Text.RegularExpressions;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Domain.Common;

/// <summary>Authoritative input rules shared by entities. Every failure is VALIDATION_FAILED naming the field.</summary>
public static partial class Guard
{
    public const int UrlMaxLength = 2048;

    public static string Required(string? value, string field, int maxLength)
    {
        var trimmed = value?.Trim() ?? "";
        if (trimmed.Length == 0)
            throw Invalid(field, "is required");
        return trimmed.Length <= maxLength ? trimmed : throw Invalid(field, $"must be at most {maxLength} characters");
    }

    public static string? Optional(string? value, string field, int maxLength)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : Required(trimmed, field, maxLength);
    }

    public static string LanguageTag(string? value, string field)
    {
        try
        {
            var name = CultureInfo.GetCultureInfo(value?.Trim() ?? "", predefinedOnly: true).Name;
            return name.Length > 0 ? name : throw Invalid(field, "is required");
        }
        catch (CultureNotFoundException)
        {
            throw Invalid(field, $"'{value}' is not a supported language tag (for example 'en' or 'en-US')");
        }
    }

    /// <summary>Absolute http(s) URL (https only when <paramref name="httpsOnly"/>) without embedded credentials, or null when blank.</summary>
    public static string? OptionalHttpUrl(string? value, string field, bool httpsOnly = false)
    {
        var trimmed = Optional(value, field, UrlMaxLength);
        if (trimmed is null)
            return null;
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && (httpsOnly || uri.Scheme != Uri.UriSchemeHttp))
            || string.IsNullOrEmpty(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw Invalid(field, httpsOnly ? "must be an absolute https URL" : "must be an absolute http or https URL");
        }
        return trimmed;
    }

    /// <summary>Trims items, drops blanks and case-insensitive duplicates, and enforces count and length limits.</summary>
    public static List<string> TextList(IEnumerable<string>? values, string field, int maxItems, int maxItemLength)
    {
        var items = (values ?? []).Select(v => v?.Trim() ?? "").Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (items.Count > maxItems)
            throw Invalid(field, $"must contain at most {maxItems} items");
        if (items.FirstOrDefault(i => i.Length > maxItemLength) is { } tooLong)
            throw Invalid(field, $"item '{tooLong[..Math.Min(20, tooLong.Length)]}...' must be at most {maxItemLength} characters");
        return items;
    }

    /// <summary>Normalizes hashtags to '#word' form; letters, digits and underscores only.</summary>
    public static List<string> Hashtags(IEnumerable<string>? values, string field, int maxItems)
    {
        var tags = TextList(values?.Select(v => v?.Trim().TrimStart('#') ?? ""), field, maxItems, maxItemLength: 100);
        if (tags.FirstOrDefault(t => !HashtagBody().IsMatch(t)) is { } bad)
            throw Invalid(field, $"'{bad}' is not a valid hashtag (letters, digits and underscores only)");
        return tags.Select(t => "#" + t).ToList();
    }

    public static TEnum Defined<TEnum>(TEnum value, string field) where TEnum : struct, Enum =>
        Enum.IsDefined(value) ? value : throw Invalid(field, $"has unsupported value '{value}'");

    public static BusinessException Invalid(string field, string problem) =>
        new(ErrorCodes.ValidationFailed, $"{field} {problem}.");

    [GeneratedRegex(@"^[\p{L}\p{N}_]+$")]
    private static partial Regex HashtagBody();
}
