using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Infrastructure.Persistence.Configurations;

internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("Organizations");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.Name).HasMaxLength(Organization.NameMaxLength).IsRequired();
        builder.Property(o => o.Industry).HasMaxLength(Organization.IndustryMaxLength).IsRequired();
        builder.Property(o => o.DefaultLanguage).HasMaxLength(35).IsRequired();
        builder.Property(o => o.TimeZoneId).HasMaxLength(64).IsRequired();
        builder.Property(o => o.CreatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(o => o.UpdatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(o => o.RowVersion).IsRowVersion();
        builder.IsEnumString<Organization, OrganizationStatus>(o => o.Status, "Status");
    }
}
