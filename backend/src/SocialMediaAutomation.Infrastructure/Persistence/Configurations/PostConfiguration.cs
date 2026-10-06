using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Infrastructure.Persistence.Configurations;

internal sealed class PostConfiguration : IEntityTypeConfiguration<Post>
{
    public void Configure(EntityTypeBuilder<Post> builder)
    {
        builder.ToTable("Posts");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.CreatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(p => p.UpdatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(p => p.RowVersion).IsRowVersion();
        builder.IsEnumString<Post, SocialPlatform>(p => p.Platform, "Platform");
        builder.IsEnumString<Post, EditorialState>(p => p.EditorialState, "EditorialState");

        // Optional same-tenant campaign folder: when set, (OrganizationId, CampaignId) must match a campaign of this organization.
        builder.HasOne<Campaign>().WithMany()
            .HasForeignKey(p => new { p.OrganizationId, p.CampaignId })
            .HasPrincipalKey(c => new { c.OrganizationId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMemberReference(p => new { p.OrganizationId, p.CreatedByUserId });

        // CurrentVersionId must be a version of THIS post in THIS organization: the composite FK includes the post's own Id.
        builder.HasOne<PostVersion>().WithMany()
            .HasForeignKey(p => new { p.OrganizationId, p.Id, p.CurrentVersionId })
            .HasPrincipalKey(v => new { v.OrganizationId, v.PostId, v.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(p => p.CurrentVersionId).IsUnique().HasFilter("[CurrentVersionId] IS NOT NULL");

        // The active approval must be a review of THIS post's CURRENT version: an edit that moves CurrentVersionId
        // without clearing ActiveApprovalId is rejected by the database itself (PRD §20-21).
        builder.HasOne<ReviewRequest>().WithMany()
            .HasForeignKey(p => new { p.OrganizationId, p.Id, p.CurrentVersionId, p.ActiveApprovalId })
            .HasPrincipalKey(r => new { r.OrganizationId, r.PostId, r.PostVersionId, r.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t => t.HasCheckConstraint("CK_Posts_ApprovalMatchesState",
            "([EditorialState] = 'APPROVED' AND [ActiveApprovalId] IS NOT NULL) OR ([EditorialState] <> 'APPROVED' AND [ActiveApprovalId] IS NULL)"));

        // Target of PostVersions (OrganizationId, PostId) and later review/publication rows.
        builder.HasAlternateKey(p => new { p.OrganizationId, p.Id });
    }
}
