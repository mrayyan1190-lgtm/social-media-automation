using Microsoft.EntityFrameworkCore;
using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.Media.DTOs;
using SocialMediaAutomation.Domain.Entities;

namespace SocialMediaAutomation.Infrastructure.Persistence.Repositories;

internal sealed class MediaRepository(CoreDbContext db) : IMediaRepository
{
    public Task<MediaAsset?> GetAsync(Guid mediaAssetId, CancellationToken cancellationToken) =>
        db.MediaAssets.AsNoTracking().SingleOrDefaultAsync(m => m.Id == mediaAssetId, cancellationToken);

    // ponytail: unpaged; add paging when libraries grow beyond a few hundred assets.
    public async Task<IReadOnlyList<MediaLibraryItemDto>> ListAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await db.MediaAssets.AsNoTracking()
            .Where(m => m.OrganizationId == organizationId)
            .OrderByDescending(m => m.CreatedAtUtc)
            .Select(m => new MediaLibraryItemDto(
                new MediaAssetDto(m.Id, m.OriginalFileName, m.ContentType, m.SizeBytes, m.Width, m.Height, m.Sha256, m.SourceType, m.CreatedAtUtc),
                db.PostVersions.Where(v => v.OrganizationId == organizationId && v.Images.Any(i => i.MediaAssetId == m.Id)).Select(v => v.PostId).Distinct().Count()))
            .ToListAsync(cancellationToken);

    public void Add(MediaAsset asset) => db.MediaAssets.Add(asset);
}
