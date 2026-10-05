using Microsoft.EntityFrameworkCore;
using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Domain.Entities;

namespace SocialMediaAutomation.Infrastructure.Persistence.Repositories;

internal sealed class BrandProfileRepository(CoreDbContext db) : IBrandProfileRepository
{
    public Task<BrandProfile?> GetByOrganizationAsync(Guid organizationId, CancellationToken cancellationToken) =>
        db.BrandProfiles.SingleOrDefaultAsync(p => p.OrganizationId == organizationId, cancellationToken);

    public void Add(BrandProfile profile) => db.BrandProfiles.Add(profile);
}
