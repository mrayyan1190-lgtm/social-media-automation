using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SocialMediaAutomation.Domain.Common;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Infrastructure.Persistence.Configurations;

internal sealed class PostVersionConfiguration : IEntityTypeConfiguration<PostVersion>
{
    public const string ImmutableTrigger = "TR_PostVersions_Immutable";

    public void Configure(EntityTypeBuilder<PostVersion> builder)
    {
        // Versions are append-only (AGENTS.md §16): a trigger created in the migration rejects UPDATE and DELETE.
        builder.ToTable("PostVersions", t => t.HasTrigger(ImmutableTrigger));
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();
        builder.Property(v => v.Hook).HasMaxLength(PostVersion.HookMax);
        builder.Property(v => v.Body).HasMaxLength(5000).IsRequired();
        builder.Property(v => v.CallToAction).HasMaxLength(Campaign.ShortTextMax);
        builder.Property(v => v.DestinationUrl).HasMaxLength(Guard.UrlMaxLength);
        builder.Property(v => v.AltText).HasMaxLength(PostVersion.AltTextMax);
        builder.Property(v => v.CreatedAtUtc).HasColumnType("datetime2(3)");
        builder.Ignore(v => v.RenderedLength);
        builder.IsJsonStringList(v => v.Hashtags, "Hashtags");
        builder.IsEnumString<PostVersion, PostVersionSource>(v => v.CreationSource, "CreationSource");

        builder.HasOne<Post>().WithMany()
            .HasForeignKey(v => new { v.OrganizationId, v.PostId })
            .HasPrincipalKey(p => new { p.OrganizationId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MediaAsset>().WithMany()
            .HasForeignKey(v => new { v.OrganizationId, v.MediaAssetId })
            .HasPrincipalKey(m => new { m.OrganizationId, m.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMemberReference(v => new { v.OrganizationId, v.CreatedByUserId });
        builder.HasOne<SocialAccount>().WithMany()
            .HasForeignKey(v => new { v.OrganizationId, v.SocialAccountId })
            .HasPrincipalKey(a => new { a.OrganizationId, a.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(v => new { v.PostId, v.VersionNumber }).IsUnique();
        builder.HasAlternateKey(v => new { v.OrganizationId, v.PostId, v.Id });
    }
}
