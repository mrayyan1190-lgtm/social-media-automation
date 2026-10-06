using SocialMediaAutomation.Application.Campaigns.DTOs;
using SocialMediaAutomation.Domain.Entities;

namespace SocialMediaAutomation.Application.Abstractions;

public interface ICampaignRepository
{
    Task<IReadOnlyList<CampaignSummaryDto>> ListAsync(Guid organizationId, bool includeArchived, CancellationToken cancellationToken);

    /// <summary>Loads by id only; callers must verify OrganizationId against the authorized organization.</summary>
    Task<Campaign?> GetAsync(Guid campaignId, CancellationToken cancellationToken);

    void Add(Campaign campaign);
}
