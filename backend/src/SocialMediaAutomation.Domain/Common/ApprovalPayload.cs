using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SocialMediaAutomation.Domain.Entities;

namespace SocialMediaAutomation.Domain.Common;

/// <summary>
/// The exact public payload an approval authorizes (PRD §20, ARCHITECTURE.md §12): platform, destination, text, CTA,
/// URL, hashtags, stable media identity (asset id, immutable object key, content hash) and alt text, under a payload
/// version. Serialized with a fixed field order and hashed with SHA-256. Short-lived delivery URLs are deliberately
/// absent, so regenerating a signed URL never changes what was approved.
/// </summary>
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
    string? AltText)
{
    public const string CurrentVersion = "approval-payload-v1";

    private static readonly JsonSerializerOptions Canonical = new()
    {
        // Positional record: properties serialize in declaration order, so the byte form is stable.
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static ApprovalPayload From(Post post, PostVersion version, SocialAccount account, MediaAsset? media)
    {
        if (version.PostId != post.Id || version.SocialAccountId != account.Id || version.MediaAssetId != media?.Id)
            throw new InvalidOperationException("The approval payload must be built from the version's own destination and media.");
        return new ApprovalPayload(
            CurrentVersion,
            post.Platform.ToString(),
            account.Id,
            version.Hook,
            version.Body,
            version.CallToAction,
            version.DestinationUrl,
            version.Hashtags.ToList(),
            media?.Id,
            media?.ObjectKey,
            media?.Sha256,
            version.AltText);
    }

    public string ToCanonicalJson() => JsonSerializer.Serialize(this, Canonical);

    public string ComputeHash() => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ToCanonicalJson())));

    /// <summary>True when this payload is byte-for-byte what was approved under the given hash.</summary>
    public bool Matches(string approvedHash) => string.Equals(ComputeHash(), approvedHash, StringComparison.Ordinal);
}
