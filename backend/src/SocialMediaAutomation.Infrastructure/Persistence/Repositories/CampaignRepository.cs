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
                c.Id, c.Name, c.Objective, c.PlatformSelections.Select(p => p.Platform).OrderBy(p => p).ToList(), c.Status,
                db.CampaignFacts.Count(f => f.OrganizationId == organizationId && f.CampaignId == c.Id && f.RemovedAtUtc == null),
                c.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

    public Task<Campaign?> GetAsync(Guid campaignId, CancellationToken cancellationToken) =>
        db.Campaigns.SingleOrDefaultAsync(c => c.Id == campaignId, cancellationToken);

    public Task<CampaignFact?> GetFactAsync(Guid factId, CancellationToken cancellationToken) =>
        db.CampaignFacts.SingleOrDefaultAsync(f => f.Id == factId, cancellationToken);

    public async Task<IReadOnlyList<CampaignFactDto>> ListFactsAsync(Guid organizationId, Guid campaignId, CancellationToken cancellationToken) =>
        await (from f in db.CampaignFacts.AsNoTracking()
               join u in db.Users on f.SuppliedByUserId equals u.Id
               where f.OrganizationId == organizationId && f.CampaignId == campaignId && f.RemovedAtUtc == null
               orderby f.CreatedAtUtc
               select new CampaignFactDto(f.Id, f.CampaignId, f.Text, f.SourceNote, f.SuppliedByUserId, u.DisplayName,
                   f.CreatedAtUtc, f.UpdatedAtUtc, f.RowVersion))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CampaignFact>> ListActiveFactEntitiesAsync(Guid organizationId, Guid campaignId, CancellationToken cancellationToken) =>
        await db.CampaignFacts
            .Where(f => f.OrganizationId == organizationId && f.CampaignId == campaignId && f.RemovedAtUtc == null)
            .ToListAsync(cancellationToken);

    public void Add(Campaign campaign) => db.Campaigns.Add(campaign);

    public void Add(CampaignFact fact) => db.CampaignFacts.Add(fact);
}
