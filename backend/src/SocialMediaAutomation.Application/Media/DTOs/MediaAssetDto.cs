using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Application.Media.DTOs;

/// <summary>Media metadata. Deliberately carries no URL: identity is the id, object key and content hash.</summary>
public sealed record MediaAssetDto(
    Guid Id,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    int Width,
    int Height,
    string Sha256,
    MediaSourceType SourceType,
    DateTime CreatedAtUtc)
{
    public static MediaAssetDto From(MediaAsset m) =>
        new(m.Id, m.OriginalFileName, m.ContentType, m.SizeBytes, m.Width, m.Height, m.Sha256, m.SourceType, m.CreatedAtUtc);
}

/// <summary>Media Library row: the asset plus how many distinct posts have a version that uses it.</summary>
public sealed record MediaLibraryItemDto(MediaAssetDto Asset, int UsedByPostCount);
