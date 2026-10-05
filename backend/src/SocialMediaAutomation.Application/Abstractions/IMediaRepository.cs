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
