using System.Text.RegularExpressions;

namespace SocialMediaAutomation.Domain.Common;

/// <summary>
/// Last line of defence for free-text that is stored or shown to operators (exception messages, provider errors):
/// masks bearer tokens, API keys, secrets and connection-string passwords. Callers must still never pass secrets on purpose.
/// </summary>
public static partial class SecretRedactor
{
    private const string Mask = "***";

    public static string Redact(string text) =>
        KeyValue().Replace(Bearer().Replace(ApiKey().Replace(text, Mask), $"Bearer {Mask}"), m => $"{m.Groups[1].Value}={Mask}");

    [GeneratedRegex(@"\b(?:sk|gsk|pk|rk)-[A-Za-z0-9_\-]{8,}", RegexOptions.IgnoreCase)]
    private static partial Regex ApiKey();

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9._\-~+/]+=*", RegexOptions.IgnoreCase)]
    private static partial Regex Bearer();

    [GeneratedRegex(@"\b(access_token|refresh_token|client_secret|api[_-]?key|password|pwd|secret|token)\s*[=:]\s*[^\s;&,""']+", RegexOptions.IgnoreCase)]
    private static partial Regex KeyValue();
}
