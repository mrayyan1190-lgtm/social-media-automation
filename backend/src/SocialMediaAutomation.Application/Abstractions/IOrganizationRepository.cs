using SocialMediaAutomation.Application.Organizations.DTOs;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Application.Abstractions;

public interface IOrganizationRepository
{
    Task<Organization?> GetAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>The caller's membership row in one organization, whatever its status.</summary>
    Task<OrganizationMember?> GetMembershipAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken);

    /// <summary>Loads by id only; callers must verify the member's OrganizationId against the authorized organization.</summary>
    Task<OrganizationMember?> GetMemberAsync(Guid memberId, CancellationToken cancellationToken);

    Task<int> CountActiveAdminsAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<MembershipSummaryDto>> ListActiveMembershipsForUserAsync(Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<MemberDto>> ListMembersAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> ListActiveMemberUserIdsAsync(Guid organizationId, IReadOnlyCollection<OrganizationRole> roles, CancellationToken cancellationToken);

    void Add(Organization organization);

    void Add(OrganizationMember member);
}
