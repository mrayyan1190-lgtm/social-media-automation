using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Infrastructure.Persistence.Configurations;

internal sealed class OrganizationMemberConfiguration : IEntityTypeConfiguration<OrganizationMember>
{
    public void Configure(EntityTypeBuilder<OrganizationMember> builder)
    {
        builder.ToTable("OrganizationMembers");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.CreatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(m => m.RowVersion).IsRowVersion();
        builder.Ignore(m => m.IsActive);
        builder.IsEnumString<OrganizationMember, OrganizationRole>(m => m.Role, "Role");
        builder.IsEnumString<OrganizationMember, MembershipStatus>(m => m.Status, "Status");

        builder.HasOne<Organization>().WithMany().HasForeignKey(m => m.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Restrict);

        // One membership per user per workspace; serves the per-request authorization lookup, and is the target of
        // composite FKs (OrganizationId, UserId) from tenant rows that record who acted.
        builder.HasAlternateKey(m => new { m.OrganizationId, m.UserId });
        // "Which workspaces can this user open?" (GET /me, GET /organizations).
        builder.HasIndex(m => new { m.UserId, m.Status });
        // Lets future tenant-owned tables reference a member through a composite (OrganizationId, MemberId) key,
        // so the database itself rejects a member from another organization.
        builder.HasAlternateKey(m => new { m.OrganizationId, m.Id });
    }
}
