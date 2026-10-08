using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SocialMediaAutomation.Domain.Entities;

namespace SocialMediaAutomation.Domain.Common;

/// <summary>
/// The exact public payload an approval authorizes (PRD §20, ARCHITECTURE.md §12): platform, destination, text, CTA,
/// URL, hashtags, stable media identity (asset id, immutable object key, content hash) and alt text of every image in
/// order, under a payload version. Serialized with a fixed field order and hashed with SHA-256. Short-lived delivery
/// URLs are deliberately absent, so regenerating a signed URL never changes what was approved.
/// </summary>
/// <remarks>
/// The first image keeps the v1 fields and images 2-4 go in <see cref="MoreImages"/> (v2), which is omitted when there
/// are none: a post with 0 or 1 image serializes byte-for-byte as before, so approvals made before multi-image support
/// still match their stored hash.
/// </remarks>
public sealed record ApprovalPayload(
    string PayloadVersion,
    string Platform,
    Guid SocialAccountId,
    string? Hook,
    string Body,
    string? CallToAction,
    string? DestinationUrl,
    IReadOnlyList<string> Hashtags,
    Guid? MediaAssetId,
    string? MediaObjectKey,
    string? MediaSha256,
    string? AltText,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<ApprovedImage>? MoreImages = null)
{
    public const string CurrentVersion = "approval-payload-v1";
    /// <summary>Payload version of a post with more than one image.</summary>
    public const string MultiImageVersion = "approval-payload-v2";

    private static readonly JsonSerializerOptions Canonical = new()
    {
        // Positional record: properties serialize in declaration order, so the byte form is stable.
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <param name="media">The version's images, loaded in the version's order.</param>
    public static ApprovalPayload From(Post post, PostVersion version, SocialAccount account, IReadOnlyList<MediaAsset> media)
    {
        var images = version.Images.OrderBy(i => i.Position).ToList();
        if (version.PostId != post.Id || version.SocialAccountId != account.Id
            || !images.Select(i => i.MediaAssetId).SequenceEqual(media.Select(m => m.Id)))
            throw new InvalidOperationException("The approval payload must be built from the version's own destination and media.");
        var first = media.FirstOrDefault();
        var more = media.Skip(1).Select((m, i) => new ApprovedImage(m.Id, m.ObjectKey, m.Sha256, images[i + 1].AltText)).ToList();
        return new ApprovalPayload(
            more.Count > 0 ? MultiImageVersion : CurrentVersion,
            post.Platform.ToString(),
            account.Id,
            version.Hook,
            version.Body,
            version.CallToAction,
            version.DestinationUrl,
            version.Hashtags.ToList(),
            first?.Id,
            first?.ObjectKey,
            first?.Sha256,
            images.FirstOrDefault()?.AltText,
            more.Count > 0 ? more : null);
    }

    /// <summary>Every approved image in publishing order. Derived, so never part of the hashed bytes.</summary>
    [JsonIgnore]
    public IReadOnlyList<ApprovedImage> AllImages =>
        MediaAssetId is { } id ? [new ApprovedImage(id, MediaObjectKey!, MediaSha256!, AltText), .. MoreImages ?? []] : [];

    public string ToCanonicalJson() => JsonSerializer.Serialize(this, Canonical);

    public string ComputeHash() => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ToCanonicalJson())));

    /// <summary>True when this payload is byte-for-byte what was approved under the given hash.</summary>
    public bool Matches(string approvedHash) => string.Equals(ComputeHash(), approvedHash, StringComparison.Ordinal);
}

/// <summary>Stable identity of one approved image: asset id, immutable object key, content hash and alt text.</summary>
public sealed record ApprovedImage(Guid MediaAssetId, string ObjectKey, string Sha256, string? AltText);
