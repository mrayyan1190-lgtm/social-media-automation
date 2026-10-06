using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Infrastructure.Persistence.Configurations;

internal sealed class SocialAccountConfiguration : IEntityTypeConfiguration<SocialAccount>
{
    public void Configure(EntityTypeBuilder<SocialAccount> builder)
    {
        builder.ToTable("SocialAccounts");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.DisplayName).HasMaxLength(SocialAccount.DisplayNameMax).IsRequired();
        builder.Property(a => a.ExternalAccountId).HasMaxLength(200);
        builder.Property(a => a.CreatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(a => a.UpdatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(a => a.RowVersion).IsRowVersion();
        builder.Ignore(a => a.IsEligibleDestination);
        builder.IsEnumString<SocialAccount, SocialPlatform>(a => a.Platform, "Platform");
        builder.IsEnumString<SocialAccount, ExecutionMode>(a => a.ExecutionMode, "ExecutionMode");
        builder.IsEnumString<SocialAccount, ConnectionStatus>(a => a.ConnectionStatus, "ConnectionStatus");
        builder.HasOne<Organization>().WithMany().HasForeignKey(a => a.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMemberReference(a => new { a.OrganizationId, a.CreatedByUserId });
        // Target of PostVersions (OrganizationId, SocialAccountId): a version cannot point at another workspace's destination.
        builder.HasAlternateKey(a => new { a.OrganizationId, a.Id });
        builder.HasIndex(a => new { a.OrganizationId, a.Platform });

        builder.Property(a => a.LastVerifiedAtUtc).HasColumnType("datetime2(3)");
        builder.IsJsonStringList(a => a.Capabilities, "Capabilities");
        // The grant must belong to the same workspace; connected modes always have a grant, unconnected modes never do.
        builder.HasOne<OAuthConnection>().WithMany()
            .HasForeignKey(a => new { a.OrganizationId, a.OAuthConnectionId })
            .HasPrincipalKey(c => new { c.OrganizationId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t => t.HasCheckConstraint("CK_SocialAccounts_ModeMatchesGrant",
            "([ExecutionMode] IN ('LIVE', 'TEST_ACCOUNT') AND [OAuthConnectionId] IS NOT NULL AND [ExternalAccountId] IS NOT NULL) " +
            "OR ([ExecutionMode] IN ('SIMULATED', 'NOT_CONFIGURED') AND [OAuthConnectionId] IS NULL AND [ExternalAccountId] IS NULL)"));
        // One destination per provider account per workspace, so a reconnect updates instead of duplicating.
        builder.HasIndex(a => new { a.OrganizationId, a.Platform, a.ExternalAccountId })
            .IsUnique().HasFilter("[ExternalAccountId] IS NOT NULL");
    }
}

internal sealed class OAuthConnectionConfiguration : IEntityTypeConfiguration<OAuthConnection>
{
    public void Configure(EntityTypeBuilder<OAuthConnection> builder)
    {
        builder.ToTable("OAuthConnections");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.ExternalUserId).HasMaxLength(200).IsRequired();
        builder.Property(c => c.DisplayName).HasMaxLength(SocialAccount.DisplayNameMax).IsRequired();
        // Protected (encrypted) values only; an empty access token means the grant was revoked.
        builder.Property(c => c.ProtectedAccessToken).HasMaxLength(8000).IsRequired();
        builder.Property(c => c.ProtectedRefreshToken).HasMaxLength(8000);
        builder.Property(c => c.AccessTokenExpiresAtUtc).HasColumnType("datetime2(3)");
        builder.Property(c => c.ConnectedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(c => c.UpdatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(c => c.RowVersion).IsRowVersion();
        builder.IsJsonStringList(c => c.Scopes, "Scopes");
        builder.IsEnumString<OAuthConnection, SocialPlatform>(c => c.Platform, "Platform");
        builder.IsEnumString<OAuthConnection, OAuthConnectionStatus>(c => c.Status, "Status");
        builder.HasOne<Organization>().WithMany().HasForeignKey(c => c.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMemberReference(c => new { c.OrganizationId, c.ConnectedByUserId });
        builder.HasAlternateKey(c => new { c.OrganizationId, c.Id });
        builder.HasIndex(c => new { c.OrganizationId, c.Platform, c.ExternalUserId }).IsUnique();
    }
}

internal sealed class OAuthStateConfiguration : IEntityTypeConfiguration<OAuthState>
{
    public void Configure(EntityTypeBuilder<OAuthState> builder)
    {
        builder.ToTable("OAuthStates");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.StateHash).HasMaxLength(64).IsFixedLength().IsUnicode(false).IsRequired();
        builder.Property(s => s.CreatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(s => s.ExpiresAtUtc).HasColumnType("datetime2(3)");
        builder.Property(s => s.ConsumedAtUtc).HasColumnType("datetime2(3)");
        builder.IsEnumString<OAuthState, SocialPlatform>(s => s.Platform, "Platform");
        builder.IsEnumString<OAuthState, ExecutionMode>(s => s.RequestedMode, "RequestedMode");
        builder.HasMemberReference(s => new { s.OrganizationId, s.UserId });
        builder.HasIndex(s => s.StateHash).IsUnique();
        // Consumption is a tracked update guarded by this token, so two concurrent callbacks cannot both consume one state.
        builder.Property(s => s.ConsumedAtUtc).IsConcurrencyToken();
    }
}

internal sealed class ReviewRequestConfiguration : IEntityTypeConfiguration<ReviewRequest>
{
    public void Configure(EntityTypeBuilder<ReviewRequest> builder)
    {
        builder.ToTable("ReviewRequests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.SubmissionNote).HasMaxLength(ReviewRequest.NoteMax);
        builder.Property(r => r.DecisionComment).HasMaxLength(ReviewRequest.NoteMax);
        builder.Property(r => r.ApprovedPayloadVersion).HasMaxLength(50);
        builder.Property(r => r.ApprovedPayloadHash).HasMaxLength(64).IsFixedLength().IsUnicode(false);
        builder.AddJsonCheck("ApprovedPayloadJson");
        builder.Property(r => r.SubmittedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(r => r.DecidedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(r => r.RowVersion).IsRowVersion();
        builder.IsEnumString<ReviewRequest, ReviewStatus>(r => r.Status, "Status");
        // An approval always carries its exact payload and hash.
        builder.ToTable(t => t.HasCheckConstraint("CK_ReviewRequests_ApprovedPayload",
            "[Status] <> 'APPROVED' OR ([ApprovedPayloadHash] IS NOT NULL AND [ApprovedPayloadJson] IS NOT NULL AND [DecidedByUserId] IS NOT NULL)"));
        builder.ToTable(t => t.HasCheckConstraint("CK_ReviewRequests_ChangesExplained",
            "[Status] <> 'CHANGES_REQUESTED' OR LEN(LTRIM(RTRIM([DecisionComment]))) > 0"));

        builder.HasOne<Post>().WithMany()
            .HasForeignKey(r => new { r.OrganizationId, r.PostId }).HasPrincipalKey(p => new { p.OrganizationId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        // The exact reviewed version, of the same post and workspace.
        builder.HasOne<PostVersion>().WithMany()
            .HasForeignKey(r => new { r.OrganizationId, r.PostId, r.PostVersionId }).HasPrincipalKey(v => new { v.OrganizationId, v.PostId, v.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMemberReference(r => new { r.OrganizationId, r.SubmittedByUserId });
        builder.HasMemberReference(r => new { r.OrganizationId, r.DecidedByUserId });
        builder.HasMemberReference(r => new { r.OrganizationId, r.AssignedReviewerUserId });

        builder.HasAlternateKey(r => new { r.OrganizationId, r.Id });
        builder.HasAlternateKey(r => new { r.OrganizationId, r.PostId, r.PostVersionId, r.Id });
        // At most one open review per post (ARCHITECTURE.md §36).
        builder.HasIndex(r => r.PostId).IsUnique().HasFilter("[Status] = 'PENDING'").HasDatabaseName("UX_ReviewRequests_OnePendingPerPost");
        // Review queue: by organization and status, oldest first.
        builder.HasIndex(r => new { r.OrganizationId, r.Status, r.SubmittedAtUtc });
    }
}

internal sealed class ReviewCommentConfiguration : IEntityTypeConfiguration<ReviewComment>
{
    public void Configure(EntityTypeBuilder<ReviewComment> builder)
    {
        builder.ToTable("ReviewComments");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Body).HasMaxLength(ReviewComment.BodyMax).IsRequired();
        builder.Property(c => c.CreatedAtUtc).HasColumnType("datetime2(3)");
        builder.HasOne<ReviewRequest>().WithMany()
            .HasForeignKey(c => new { c.OrganizationId, c.ReviewRequestId }).HasPrincipalKey(r => new { r.OrganizationId, r.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMemberReference(c => new { c.OrganizationId, c.AuthorUserId });
    }
}

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).ValueGeneratedNever();
        builder.Property(n => n.Title).HasMaxLength(200).IsRequired();
        builder.Property(n => n.Message).HasMaxLength(1000).IsRequired();
        builder.Property(n => n.ResourceType).HasMaxLength(50).IsRequired();
        builder.Property(n => n.CreatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(n => n.ReadAtUtc).HasColumnType("datetime2(3)");
        builder.IsEnumString<Notification, NotificationType>(n => n.Type, "Type");
        builder.HasMemberReference(n => new { n.OrganizationId, n.RecipientUserId });
        // A user's inbox: unread first, newest first (AGENTS.md §62).
        builder.HasIndex(n => new { n.RecipientUserId, n.OrganizationId, n.ReadAtUtc, n.CreatedAtUtc });
    }
}

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public const string ImmutableTrigger = "TR_AuditLogs_Immutable";

    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs", t => t.HasTrigger(ImmutableTrigger));
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Action).HasMaxLength(60).IsRequired();
        builder.Property(a => a.ResourceType).HasMaxLength(50).IsRequired();
        builder.Property(a => a.MetadataJson).IsRequired();
        builder.AddJsonCheck("MetadataJson");
        builder.Property(a => a.CreatedAtUtc).HasColumnType("datetime2(3)");
        builder.HasOne<Organization>().WithMany().HasForeignKey(a => a.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => new { a.OrganizationId, a.CreatedAtUtc });
        builder.HasIndex(a => new { a.OrganizationId, a.ResourceId });
    }
}
