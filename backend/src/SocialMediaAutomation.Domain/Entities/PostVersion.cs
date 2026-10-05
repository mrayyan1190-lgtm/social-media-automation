using SocialMediaAutomation.Domain.Common;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Domain.Entities;

/// <summary>
/// One immutable public-facing version of a Post (PRD §17). There are no mutators: every meaningful change creates
/// a new version through <see cref="Post.Revise"/>. The database also rejects UPDATE and DELETE on this table.
/// </summary>
public sealed class PostVersion
{
    public const int HookMax = 300;
    public const int AltTextMax = 1000;

    private PostVersion() { }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid PostId { get; private set; }
    public int VersionNumber { get; private set; }
    public string? Hook { get; private set; }
    public string Body { get; private set; } = null!;
    public string? CallToAction { get; private set; }
    public string? DestinationUrl { get; private set; }
    public List<string> Hashtags { get; private set; } = [];

    /// <summary>Destination account (a SocialAccount of the same workspace and platform). Required before review.</summary>
    public Guid? SocialAccountId { get; private set; }

    public Guid? MediaAssetId { get; private set; }
    public string? AltText { get; private set; }
    public PostVersionSource CreationSource { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    internal static PostVersion Create(
        Guid id, Post post, int versionNumber, PostContent content, MediaAsset? media, SocialAccount? destination, PostVersionSource source,
        Guid userId, DateTime utcNow)
    {
        var rules = PlatformRules.For(post.Platform);
        var version = new PostVersion
        {
            Id = id,
            OrganizationId = post.OrganizationId,
            PostId = post.Id,
            VersionNumber = versionNumber,
            Hook = Guard.Optional(content.Hook, "Hook", HookMax),
            Body = Guard.Required(content.Body, "Body/caption", rules.MaxTextLength),
            CallToAction = Guard.Optional(content.CallToAction, "CTA", Campaign.ShortTextMax),
            DestinationUrl = Guard.OptionalHttpUrl(content.DestinationUrl, "Destination URL"),
            Hashtags = Guard.Hashtags(content.Hashtags, "Hashtags", rules.MaxHashtags),
            AltText = Guard.Optional(content.AltText, "Alt text", AltTextMax),
            CreationSource = Guard.Defined(source, "Creation source"),
            CreatedByUserId = userId,
            CreatedAtUtc = utcNow,
        };

        if (content.MediaAssetId != media?.Id)
            throw new InvalidOperationException("The loaded media asset does not match the requested MediaAssetId.");
        if (media is not null)
        {
            // Defense in depth: the application already scopes the lookup, and a composite FK enforces it in SQL.
            if (media.OrganizationId != post.OrganizationId)
                throw new BusinessException(ErrorCodes.TenantMismatch, "The image belongs to another workspace.");
            media.EnsureSuitableFor(post.Platform);
            version.MediaAssetId = media.Id;
        }
        else if (version.AltText is not null)
        {
            throw Guard.Invalid("Alt text", "requires an attached image");
        }

        if (content.SocialAccountId != destination?.Id)
            throw new InvalidOperationException("The loaded destination does not match the requested SocialAccountId.");
        if (destination is not null)
        {
            if (destination.OrganizationId != post.OrganizationId)
                throw new BusinessException(ErrorCodes.TenantMismatch, "The destination belongs to another workspace.");
            if (destination.Platform != post.Platform)
                throw Guard.Invalid("Destination", $"is a {destination.Platform} account but this is a {post.Platform} post");
            version.SocialAccountId = destination.Id;
        }

        if (version.RenderedLength > rules.MaxTextLength)
            throw Guard.Invalid("Post text", $"is {version.RenderedLength} characters including hook, CTA, URL and hashtags; {post.Platform} allows {rules.MaxTextLength}");
        return version;
    }

    /// <summary>Approximate published length: hook, body, CTA, URL and hashtags joined by blank lines.</summary>
    public int RenderedLength =>
        new[] { Hook, Body, CallToAction, DestinationUrl, Hashtags.Count > 0 ? string.Join(' ', Hashtags) : null }
            .Where(part => part is not null)
            .Sum(part => part!.Length + 2) - 2;

    /// <summary>True when the given content would publish exactly what this version publishes.</summary>
    public bool HasSameContentAs(PostVersion other) =>
        Hook == other.Hook && Body == other.Body && CallToAction == other.CallToAction && DestinationUrl == other.DestinationUrl
        && Hashtags.SequenceEqual(other.Hashtags) && SocialAccountId == other.SocialAccountId
        && MediaAssetId == other.MediaAssetId && AltText == other.AltText;
}

/// <summary>Editable public-facing content of a post version. Media is referenced by id and validated against the loaded asset.</summary>
public sealed record PostContent(
    string? Hook,
    string Body,
    string? CallToAction,
    string? DestinationUrl,
    IReadOnlyList<string>? Hashtags,
    Guid? MediaAssetId,
    string? AltText,
    Guid? SocialAccountId = null);
