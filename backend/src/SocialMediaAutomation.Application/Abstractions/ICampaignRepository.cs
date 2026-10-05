using SocialMediaAutomation.Application.Campaigns.DTOs;
using SocialMediaAutomation.Domain.Entities;

namespace SocialMediaAutomation.Application.Abstractions;

public interface ICampaignRepository
{
    Task<IReadOnlyList<CampaignSummaryDto>> ListAsync(Guid organizationId, bool includeArchived, CancellationToken cancellationToken);

    /// <summary>Loads by id only; callers must verify OrganizationId against the authorized organization.</summary>
    Task<Campaign?> GetAsync(Guid campaignId, CancellationToken cancellationToken);

    /// <summary>Loads by id only, including removed facts; callers must verify organization and campaign ownership.</summary>
    Task<CampaignFact?> GetFactAsync(Guid factId, CancellationToken cancellationToken);

    /// <summary>Active (not removed) facts of one campaign, oldest first, with the supplier's display name.</summary>
    Task<IReadOnlyList<CampaignFactDto>> ListFactsAsync(Guid organizationId, Guid campaignId, CancellationToken cancellationToken);

    /// <summary>Tracked active facts of one campaign, for replacing the fact set.</summary>
    Task<IReadOnlyList<CampaignFact>> ListActiveFactEntitiesAsync(Guid organizationId, Guid campaignId, CancellationToken cancellationToken);

    void Add(Campaign campaign);

    void Add(CampaignFact fact);
}
