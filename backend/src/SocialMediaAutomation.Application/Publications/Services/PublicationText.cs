using SocialMediaAutomation.Domain.Common;

namespace SocialMediaAutomation.Application.Publications.Services;

/// <summary>
/// Turns the frozen approved payload into post text: hook, body, call to action (+ URL), hashtags, separated by blank
/// lines. Providers plug in their own escaping and hashtag syntax; the words themselves are exactly what was approved.
/// </summary>
public static class PublicationText
{
    public static string Compose(ApprovalPayload payload, bool includeUrl, Func<string, string>? escape = null, Func<string, string>? hashtag = null)
    {
        escape ??= text => text;
        hashtag ??= tag => $"#{tag}";
        var cta = string.Join(' ', new[] { payload.CallToAction is { Length: > 0 } c ? escape(c) : null, includeUrl ? payload.DestinationUrl : null }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
        var parts = new[]
        {
            payload.Hook is { Length: > 0 } hook ? escape(hook) : null,
            escape(payload.Body),
            cta,
            // Stored hashtags carry their "#" ("#Centaiva"); providers get the bare word and add their own syntax.
            payload.Hashtags.Count > 0 ? string.Join(' ', payload.Hashtags.Select(tag => hashtag(tag.TrimStart('#')))) : null,
        };
        return string.Join("\n\n", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }
}
