namespace SocialMediaAutomation.Api.Extensions;

/// <summary>Row versions travel as strong ETags; mutations send the last seen value back in If-Match.</summary>
internal static class RowVersionHeaders
{
    public static string ToETag(byte[] rowVersion) => $"\"{Convert.ToBase64String(rowVersion)}\"";

    /// <summary>Returns null when If-Match is absent or malformed; the application layer then rejects the change.</summary>
    public static byte[]? ReadIfMatch(HttpRequest request)
    {
        var value = request.Headers.IfMatch.ToString().Trim();
        if (value.Length < 2 || value[0] != '"' || value[^1] != '"')
            return null;

        var buffer = new byte[value.Length];
        return Convert.TryFromBase64String(value[1..^1], buffer, out var written) ? buffer[..written] : null;
    }
}
