using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SocialMediaAutomation.Domain.Common;
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
        builder.Property(c => c.Description).HasMaxLength(Campaign.LongTextMax).IsRequired();
        builder.Property(c => c.TargetAudience).HasMaxLength(Campaign.LongTextMax).IsRequired();
        builder.Property(c => c.CallToActionUrl).HasMaxLength(Guard.UrlMaxLength);
        builder.Property(c => c.CreatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(c => c.UpdatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(c => c.RowVersion).IsRowVersion();
        builder.Ignore(c => c.IsArchived);
        builder.Ignore(c => c.Platforms);
        builder.IsEnumString<Campaign, CampaignObjective>(c => c.Objective, "Objective");
        builder.IsEnumString<Campaign, CallToActionType>(c => c.CallToAction, "CallToAction");
        builder.IsEnumString<Campaign, BrandTone>(c => c.ToneOverride, "ToneOverride");
        builder.IsEnumString<Campaign, ContentLength>(c => c.ContentLength, "ContentLength");
        builder.IsEnumString<Campaign, HashtagMode>(c => c.HashtagMode, "HashtagMode");
        builder.IsEnumString<Campaign, EmojiMode>(c => c.EmojiMode, "EmojiMode");
        builder.IsEnumString<Campaign, CampaignStatus>(c => c.Status, "Status");

        builder.HasOne<Organization>().WithMany().HasForeignKey(c => c.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMemberReference(c => new { c.OrganizationId, c.CreatedByUserId });
        builder.HasMemberReference(c => new { c.OrganizationId, c.UpdatedByUserId });

        // Target of composite FKs from child rows (platforms, facts, posts) so a child cannot point at another tenant's campaign.
        builder.HasAlternateKey(c => new { c.OrganizationId, c.Id });
        // Campaign list: by organization, most recently updated first (AGENTS.md §62).
        builder.HasIndex(c => new { c.OrganizationId, c.UpdatedAtUtc });

        // Platforms are part of the aggregate: always loaded with the campaign; deselected rows are deleted.
        builder.HasMany(c => c.PlatformSelections).WithOne()
            .HasForeignKey(p => new { p.OrganizationId, p.CampaignId })
            .HasPrincipalKey(c => new { c.OrganizationId, c.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.PlatformSelections).HasField("platformSelections").AutoInclude();
    }
}

internal sealed class CampaignPlatformConfiguration : IEntityTypeConfiguration<CampaignPlatform>
{
    public void Configure(EntityTypeBuilder<CampaignPlatform> builder)
    {
        builder.ToTable("CampaignPlatforms");
        builder.HasKey(p => new { p.CampaignId, p.Platform });
        builder.IsEnumString<CampaignPlatform, SocialPlatform>(p => p.Platform, "Platform");
    }
}
