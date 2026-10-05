using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SocialMediaAutomation.Domain.Common;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Infrastructure.Persistence.Configurations;

internal sealed class BrandProfileConfiguration : IEntityTypeConfiguration<BrandProfile>
{
    public void Configure(EntityTypeBuilder<BrandProfile> builder)
    {
        builder.ToTable("BrandProfiles");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.BusinessDescription).HasMaxLength(BrandProfile.LongTextMax).IsRequired();
        builder.Property(p => p.TargetAudience).HasMaxLength(BrandProfile.LongTextMax).IsRequired();
        builder.Property(p => p.Website).HasMaxLength(Guard.UrlMaxLength);
        builder.Property(p => p.CreatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(p => p.UpdatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(p => p.RowVersion).IsRowVersion();
        builder.IsEnumString<BrandProfile, BrandTone>(p => p.BrandTone, "BrandTone");
        builder.IsJsonStringList(p => p.ProhibitedTerms, "ProhibitedTerms");

        // One brand per workspace in the MVP (PRD §39.2); the unique index also blocks a concurrent second create.
        builder.HasOne<Organization>().WithOne().HasForeignKey<BrandProfile>(p => p.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(p => p.OrganizationId).IsUnique();
        builder.HasMemberReference(p => new { p.OrganizationId, p.UpdatedByUserId });
    }
}
