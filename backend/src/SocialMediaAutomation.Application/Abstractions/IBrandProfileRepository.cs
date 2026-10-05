using SocialMediaAutomation.Domain.Entities;

namespace SocialMediaAutomation.Application.Abstractions;

public interface IBrandProfileRepository
{
    Task<BrandProfile?> GetByOrganizationAsync(Guid organizationId, CancellationToken cancellationToken);

    void Add(BrandProfile profile);
}
