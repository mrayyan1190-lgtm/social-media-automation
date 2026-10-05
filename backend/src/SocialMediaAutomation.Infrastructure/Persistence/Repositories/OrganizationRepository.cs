using Microsoft.EntityFrameworkCore;
using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.Organizations.DTOs;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Infrastructure.Persistence.Repositories;

internal sealed class OrganizationRepository(CoreDbContext db) : IOrganizationRepository
{
    public Task<Organization?> GetAsync(Guid organizationId, CancellationToken cancellationToken) =>
        db.Organizations.SingleOrDefaultAsync(o => o.Id == organizationId, cancellationToken);

    public Task<OrganizationMember?> GetMembershipAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) =>
        db.OrganizationMembers.SingleOrDefaultAsync(m => m.OrganizationId == organizationId && m.UserId == userId, cancellationToken);

    public Task<OrganizationMember?> GetMemberAsync(Guid memberId, CancellationToken cancellationToken) =>
        db.OrganizationMembers.SingleOrDefaultAsync(m => m.Id == memberId, cancellationToken);

    public Task<int> CountActiveAdminsAsync(Guid organizationId, CancellationToken cancellationToken) =>
        db.OrganizationMembers.CountAsync(
            m => m.OrganizationId == organizationId && m.Role == OrganizationRole.Admin && m.Status == MembershipStatus.Active,
            cancellationToken);

    public async Task<IReadOnlyList<MembershipSummaryDto>> ListActiveMembershipsForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await (from m in db.OrganizationMembers.AsNoTracking()
               join o in db.Organizations on m.OrganizationId equals o.Id
               where m.UserId == userId && m.Status == MembershipStatus.Active
               orderby o.Name
               select new MembershipSummaryDto(o.Id, o.Name, m.Role))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MemberDto>> ListMembersAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await (from m in db.OrganizationMembers.AsNoTracking()
               join u in db.Users on m.UserId equals u.Id
               where m.OrganizationId == organizationId
               orderby u.DisplayName
               select new MemberDto(m.Id, u.Id, u.Email, u.DisplayName, m.Role, m.Status, m.RowVersion))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListActiveMemberUserIdsAsync(Guid organizationId, IReadOnlyCollection<OrganizationRole> roles, CancellationToken cancellationToken) =>
        await db.OrganizationMembers.AsNoTracking()
            .Where(m => m.OrganizationId == organizationId && m.Status == MembershipStatus.Active && roles.Contains(m.Role))
            .Select(m => m.UserId)
            .ToListAsync(cancellationToken);

    public void Add(Organization organization) => db.Organizations.Add(organization);

    public void Add(OrganizationMember member) => db.OrganizationMembers.Add(member);
}
