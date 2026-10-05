using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.Organizations.DTOs;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Application.Tests;

internal sealed class FakeCurrentUser(Guid? userId) : ICurrentUser
{
    public Guid? UserId { get; } = userId;
}

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int Saves { get; private set; }

    public void DiscardChanges() { }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saves++;
        return Task.CompletedTask;
    }
}

/// <summary>In-memory store with the same lookup semantics as the EF repository.</summary>
internal sealed class FakeOrganizationRepository : IOrganizationRepository
{
    public List<Organization> Organizations { get; } = [];
    public List<OrganizationMember> Members { get; } = [];

    public OrganizationMember AddMember(Guid organizationId, Guid userId, OrganizationRole role)
    {
        var member = OrganizationMember.Create(Guid.NewGuid(), organizationId, userId, role, DateTime.UtcNow);
        Members.Add(member);
        return member;
    }

    public Task<Organization?> GetAsync(Guid organizationId, CancellationToken cancellationToken) =>
        Task.FromResult(Organizations.SingleOrDefault(o => o.Id == organizationId));

    public Task<OrganizationMember?> GetMembershipAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Members.SingleOrDefault(m => m.OrganizationId == organizationId && m.UserId == userId));

    public Task<OrganizationMember?> GetMemberAsync(Guid memberId, CancellationToken cancellationToken) =>
        Task.FromResult(Members.SingleOrDefault(m => m.Id == memberId));

    public Task<int> CountActiveAdminsAsync(Guid organizationId, CancellationToken cancellationToken) =>
        Task.FromResult(Members.Count(m => m.OrganizationId == organizationId && m.Role == OrganizationRole.Admin && m.IsActive));

    public Task<IReadOnlyList<MembershipSummaryDto>> ListActiveMembershipsForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MembershipSummaryDto>>(
            Members.Where(m => m.UserId == userId && m.IsActive).Select(m => new MembershipSummaryDto(m.OrganizationId, "", m.Role)).ToList());

    public Task<IReadOnlyList<MemberDto>> ListMembersAsync(Guid organizationId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MemberDto>>(
            Members.Where(m => m.OrganizationId == organizationId)
                .Select(m => new MemberDto(m.Id, m.UserId, "", "", m.Role, m.Status, m.RowVersion)).ToList());

    public Task<IReadOnlyList<Guid>> ListActiveMemberUserIdsAsync(Guid organizationId, IReadOnlyCollection<OrganizationRole> roles, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(Members.Where(m => m.OrganizationId == organizationId && m.IsActive && roles.Contains(m.Role)).Select(m => m.UserId).ToList());

    public void Add(Organization organization) => Organizations.Add(organization);

    public void Add(OrganizationMember member) => Members.Add(member);
}
