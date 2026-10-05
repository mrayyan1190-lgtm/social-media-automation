using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Infrastructure.Persistence.Configurations;

internal sealed class MediaAssetConfiguration : IEntityTypeConfiguration<MediaAsset>
{
    public const string ImmutableTrigger = "TR_MediaAssets_Immutable";

    public void Configure(EntityTypeBuilder<MediaAsset> builder)
    {
        // The trigger (created in the migration) rejects UPDATE/DELETE; declaring it stops EF using OUTPUT clauses here.
        builder.ToTable("MediaAssets", t => t.HasTrigger(ImmutableTrigger));
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.OriginalFileName).HasMaxLength(MediaAsset.FileNameMax).IsRequired();
        builder.Property(m => m.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(m => m.Sha256).HasMaxLength(64).IsFixedLength().IsUnicode(false).IsRequired();
        builder.Property(m => m.ObjectKey).HasMaxLength(300).IsUnicode(false).IsRequired();
        builder.Property(m => m.CreatedAtUtc).HasColumnType("datetime2(3)");
        builder.Ignore(m => m.AspectRatio);
        builder.IsEnumString<MediaAsset, MediaSourceType>(m => m.SourceType, "SourceType");
        builder.Property(m => m.GenerationProvider).HasMaxLength(50);
        builder.Property(m => m.GenerationModel).HasMaxLength(AiProviderSettings.ModelMax);

        builder.HasIndex(m => m.ObjectKey).IsUnique();
        builder.HasOne<Organization>().WithMany().HasForeignKey(m => m.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMemberReference(m => new { m.OrganizationId, m.CreatedByUserId });
        // Target of PostVersions (OrganizationId, MediaAssetId): a version cannot reference another workspace's image.
        builder.HasAlternateKey(m => new { m.OrganizationId, m.Id });
    }
}
