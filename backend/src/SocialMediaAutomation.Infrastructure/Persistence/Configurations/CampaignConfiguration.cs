using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Infrastructure.Persistence.Configurations;

internal sealed class CampaignConfiguration : IEntityTypeConfiguration<Campaign>
{
    public void Configure(EntityTypeBuilder<Campaign> builder)
    {
        builder.ToTable("Campaigns");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Name).HasMaxLength(Campaign.ShortTextMax).IsRequired();
        builder.Property(c => c.Color).HasMaxLength(7).IsUnicode(false).IsRequired().HasDefaultValue(Campaign.DefaultColor);
        builder.Property(c => c.Description).HasMaxLength(Campaign.LongTextMax);
        builder.Property(c => c.CreatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(c => c.UpdatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(c => c.RowVersion).IsRowVersion();
        builder.Ignore(c => c.IsArchived);
        builder.IsEnumString<Campaign, CampaignStatus>(c => c.Status, "Status");
        builder.ToTable(t => t.HasCheckConstraint("CK_Campaigns_Color", "[Color] LIKE '#[0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F]'"));

        builder.HasOne<Organization>().WithMany().HasForeignKey(c => c.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMemberReference(c => new { c.OrganizationId, c.CreatedByUserId });
        builder.HasMemberReference(c => new { c.OrganizationId, c.UpdatedByUserId });

        // Target of composite FKs from posts and AI requests so a row cannot point at another tenant's campaign.
        builder.HasAlternateKey(c => new { c.OrganizationId, c.Id });
        // Campaign list: by organization, most recently updated first (AGENTS.md §62).
        builder.HasIndex(c => new { c.OrganizationId, c.UpdatedAtUtc });
    }
}
