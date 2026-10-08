using SocialMediaAutomation.Domain.Entities;

namespace SocialMediaAutomation.Application.Abstractions;

public interface IMediaRepository
{
    /// <summary>Loads by id only; callers must verify OrganizationId against the authorized organization.</summary>
    Task<MediaAsset?> GetAsync(Guid mediaAssetId, CancellationToken cancellationToken);

    /// <summary>The organization's assets, newest first, with the number of posts that reference each.</summary>
    Task<IReadOnlyList<Media.DTOs.MediaLibraryItemDto>> ListAsync(Guid organizationId, CancellationToken cancellationToken);

    void Add(MediaAsset asset);
}

public static class MediaRepositoryExtensions
{
    /// <summary>
    /// The version's images in publishing order. Returns null when any of them is missing or belongs to another
    /// organization, so callers treat the whole image set as invalid.
    /// </summary>
    public static async Task<IReadOnlyList<MediaAsset>?> GetImagesAsync(
        this IMediaRepository media, PostVersion version, CancellationToken cancellationToken)
    {
        var images = new List<MediaAsset>();
        foreach (var image in version.Images.OrderBy(i => i.Position))
        {
            var asset = await media.GetAsync(image.MediaAssetId, cancellationToken);
            if (asset is null || asset.OrganizationId != version.OrganizationId)
                return null;
            images.Add(asset);
        }
        return images;
    }
}
