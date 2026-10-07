using Microsoft.EntityFrameworkCore;
using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.Media.DTOs;
using SocialMediaAutomation.Application.Posts.DTOs;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Infrastructure.Persistence.Repositories;

internal sealed class PostRepository(CoreDbContext db) : IPostRepository
{
    public async Task<IReadOnlyDictionary<EditorialState, int>> CountByEditorialStateAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await db.Posts.Where(p => p.OrganizationId == organizationId)
            .GroupBy(p => p.EditorialState).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);

    public Task<Post?> GetAsync(Guid postId, CancellationToken cancellationToken) =>
        db.Posts.SingleOrDefaultAsync(p => p.Id == postId, cancellationToken);

    public Task<PostVersion?> GetVersionAsync(Guid organizationId, Guid postId, Guid versionId, CancellationToken cancellationToken) =>
        db.PostVersions.AsNoTracking()
            .SingleOrDefaultAsync(v => v.OrganizationId == organizationId && v.PostId == postId && v.Id == versionId, cancellationToken);

    // ponytail: unpaged; add paging and server-side filters when a workspace grows to thousands of posts.
    public async Task<IReadOnlyList<PostListItemDto>> ListAsync(Guid organizationId, Guid? campaignId, CancellationToken cancellationToken)
    {
        var rows = await (from p in db.Posts.AsNoTracking()
                          join v in db.PostVersions on p.CurrentVersionId equals v.Id
                          join c in db.Campaigns on p.CampaignId equals c.Id into folders
                          from c in folders.DefaultIfEmpty()
                          join u in db.Users on p.CreatedByUserId equals u.Id
                          where p.OrganizationId == organizationId && p.EditorialState != EditorialState.ARCHIVED
                              && (campaignId == null || p.CampaignId == campaignId)
                          let job = db.PublicationJobs
                              .Where(j => j.OrganizationId == organizationId && j.PostId == p.Id)
                              .OrderByDescending(j => j.CreatedAtUtc)
                              .Select(j => new { j.Id, j.DeliveryState, j.PublishAtUtc })
                              .FirstOrDefault()
                          orderby p.UpdatedAtUtc descending
                          select new
                          {
                              p.Id, p.CampaignId, CampaignName = c == null ? null : c.Name, CampaignColor = c == null ? null : c.Color, p.Platform, p.EditorialState, v.VersionNumber, v.Hook, v.Body,
                              v.SocialAccountId, p.CreatedByUserId, CreatedByName = u.DisplayName, p.CreatedAtUtc, p.UpdatedAtUtc,
                              JobId = (Guid?)job.Id, JobState = (DeliveryState?)job.DeliveryState, JobPublishAt = (DateTime?)job.PublishAtUtc,
                              v.MediaAssetId,
                          })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new PostListItemDto(
                r.Id, r.CampaignId, r.CampaignName, r.CampaignColor, r.Platform, r.EditorialState, r.VersionNumber, r.Hook,
                r.Body.Length > 200 ? r.Body[..200] : r.Body, r.SocialAccountId, r.CreatedByUserId, r.CreatedByName, r.CreatedAtUtc,
                r.UpdatedAtUtc, r.JobId, r.JobState, r.JobPublishAt, r.MediaAssetId))
            .ToList();
    }

    public async Task<IReadOnlyList<PostVersionDto>> ListVersionsAsync(Guid organizationId, Guid postId, CancellationToken cancellationToken) =>
        await (from v in db.PostVersions.AsNoTracking()
               join u in db.Users on v.CreatedByUserId equals u.Id
               join m in db.MediaAssets on v.MediaAssetId equals m.Id into media
               from m in media.DefaultIfEmpty()
               where v.OrganizationId == organizationId && v.PostId == postId
               orderby v.VersionNumber descending
               select new PostVersionDto(
                   v.Id, v.PostId, v.VersionNumber, v.Hook, v.Body, v.CallToAction, v.DestinationUrl, v.Hashtags,
                   v.SocialAccountId, v.MediaAssetId, v.AltText, v.CreationSource, v.CreatedByUserId, u.DisplayName, v.CreatedAtUtc,
                   m == null ? null : new MediaAssetDto(m.Id, m.OriginalFileName, m.ContentType, m.SizeBytes, m.Width, m.Height,
                       m.Sha256, m.SourceType, m.CreatedAtUtc)))
            .ToListAsync(cancellationToken);

    public void Add(Post post) => db.Posts.Add(post);

    public void Add(PostVersion version) => db.PostVersions.Add(version);
}
