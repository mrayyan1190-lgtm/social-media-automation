using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SocialMediaAutomation.Domain.Common;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Infrastructure.Persistence.Configurations;

internal sealed class PostVersionConfiguration : IEntityTypeConfiguration<PostVersion>
{
    public const string ImmutableTrigger = "TR_PostVersions_Immutable";
    public const string ImagesImmutableTrigger = "TR_PostVersionImages_Immutable";

    public void Configure(EntityTypeBuilder<PostVersion> builder)
    {
        // Versions are append-only (AGENTS.md §16): a trigger created in the migration rejects UPDATE and DELETE.
        builder.ToTable("PostVersions", t => t.HasTrigger(ImmutableTrigger));
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();
        builder.Property(v => v.Hook).HasMaxLength(PostVersion.HookMax);
        builder.Property(v => v.Body).HasMaxLength(5000).IsRequired();
        builder.Property(v => v.CallToAction).HasMaxLength(PostVersion.CallToActionMax);
        builder.Property(v => v.DestinationUrl).HasMaxLength(Guard.UrlMaxLength);
        builder.Property(v => v.CreatedAtUtc).HasColumnType("datetime2(3)");
        builder.Ignore(v => v.RenderedLength);
        builder.Ignore(v => v.Cover);
        builder.IsJsonStringList(v => v.Hashtags, "Hashtags");
        builder.IsEnumString<PostVersion, PostVersionSource>(v => v.CreationSource, "CreationSource");

        builder.HasOne<Post>().WithMany()
            .HasForeignKey(v => new { v.OrganizationId, v.PostId })
            .HasPrincipalKey(p => new { p.OrganizationId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        // Images are part of the immutable version: inserted with it, then never changed (trigger created in the migration).
        builder.OwnsMany(v => v.Images, images =>
        {
            images.ToTable("PostVersionImages", t =>
            {
                t.HasTrigger(ImagesImmutableTrigger);
                t.HasCheckConstraint("CK_PostVersionImages_Position", $"[Position] >= 0 AND [Position] < {PlatformRules.MaxImages}");
            });
            images.WithOwner().HasForeignKey("PostVersionId");
            images.HasKey("PostVersionId", nameof(PostVersionImage.Position));
            images.Property(i => i.Position).ValueGeneratedNever();
            images.Property(i => i.AltText).HasMaxLength(PostVersion.AltTextMax);
            // Composite FK: a version can never reference another workspace's image.
            images.HasOne<MediaAsset>().WithMany()
                .HasForeignKey(i => new { i.OrganizationId, i.MediaAssetId })
                .HasPrincipalKey(m => new { m.OrganizationId, m.Id })
                .OnDelete(DeleteBehavior.Restrict);
            images.HasIndex("PostVersionId", nameof(PostVersionImage.MediaAssetId)).IsUnique();
        });
        builder.HasMemberReference(v => new { v.OrganizationId, v.CreatedByUserId });
        builder.HasOne<SocialAccount>().WithMany()
            .HasForeignKey(v => new { v.OrganizationId, v.SocialAccountId })
            .HasPrincipalKey(a => new { a.OrganizationId, a.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(v => new { v.PostId, v.VersionNumber }).IsUnique();
        builder.HasAlternateKey(v => new { v.OrganizationId, v.PostId, v.Id });
    }
}
