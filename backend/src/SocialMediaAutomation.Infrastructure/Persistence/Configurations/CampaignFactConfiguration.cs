using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SocialMediaAutomation.Domain.Entities;

namespace SocialMediaAutomation.Infrastructure.Persistence.Configurations;

internal sealed class CampaignFactConfiguration : IEntityTypeConfiguration<CampaignFact>
{
    public void Configure(EntityTypeBuilder<CampaignFact> builder)
    {
        builder.ToTable("CampaignFacts");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).ValueGeneratedNever();
        builder.Property(f => f.Text).HasMaxLength(CampaignFact.TextMax).IsRequired();
        builder.Property(f => f.SourceNote).HasMaxLength(CampaignFact.SourceNoteMax);
        builder.Property(f => f.CreatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(f => f.UpdatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(f => f.RemovedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(f => f.RowVersion).IsRowVersion();
        builder.Ignore(f => f.IsRemoved);

        // Same-tenant relationship: (OrganizationId, CampaignId) must match a campaign of the same organization.
        // This FK's index also serves "facts of a campaign".
        builder.HasOne<Campaign>().WithMany()
            .HasForeignKey(f => new { f.OrganizationId, f.CampaignId })
            .HasPrincipalKey(c => new { c.OrganizationId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMemberReference(f => new { f.OrganizationId, f.SuppliedByUserId });
    }
}
