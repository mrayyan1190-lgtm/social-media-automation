using Microsoft.EntityFrameworkCore;
using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.Campaigns.DTOs;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Infrastructure.Persistence.Repositories;

internal sealed class CampaignRepository(CoreDbContext db) : ICampaignRepository
{
    public async Task<IReadOnlyList<CampaignSummaryDto>> ListAsync(Guid organizationId, bool includeArchived, CancellationToken cancellationToken) =>
        await db.Campaigns.AsNoTracking()
            .Where(c => c.OrganizationId == organizationId && (includeArchived || c.Status != CampaignStatus.Archived))
            .OrderByDescending(c => c.UpdatedAtUtc)
            .Select(c => new CampaignSummaryDto(
                c.Id, c.Name, c.Color, c.Description, c.Status,
                db.Posts.Count(p => p.OrganizationId == organizationId && p.CampaignId == c.Id && p.EditorialState != EditorialState.ARCHIVED),
                c.UpdatedAtUtc, c.CreatedByUserId))
            .ToListAsync(cancellationToken);

    public Task<Campaign?> GetAsync(Guid campaignId, CancellationToken cancellationToken) =>
        db.Campaigns.SingleOrDefaultAsync(c => c.Id == campaignId, cancellationToken);

    public void Add(Campaign campaign) => db.Campaigns.Add(campaign);
}
