using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Infrastructure.Persistence.Configurations;

internal sealed class PublicationJobConfiguration : IEntityTypeConfiguration<PublicationJob>
{
    public void Configure(EntityTypeBuilder<PublicationJob> builder)
    {
        builder.ToTable("PublicationJobs");
        builder.HasKey(j => j.Id);
        builder.Property(j => j.Id).ValueGeneratedNever();
        builder.Property(j => j.PayloadVersion).HasMaxLength(50).IsRequired();
        builder.Property(j => j.PayloadJson).IsRequired();
        builder.AddJsonCheck("PayloadJson");
        builder.Property(j => j.PayloadHash).HasMaxLength(64).IsFixedLength().IsUnicode(false).IsRequired();
        builder.Property(j => j.TimeZoneId).HasMaxLength(100);
        builder.Property(j => j.LockedBy).HasMaxLength(200);
        builder.Property(j => j.CorrelationId).HasMaxLength(SocialMediaAutomation.Domain.Common.CorrelationIds.MaxLength);
        builder.Property(j => j.ProviderContainerId).HasMaxLength(200);
        builder.Property(j => j.ExternalPostId).HasMaxLength(200);
        builder.Property(j => j.ExternalPostUrl).HasMaxLength(2048);
        builder.Property(j => j.LastErrorCode).HasMaxLength(100);
        builder.Property(j => j.LastErrorMessage).HasMaxLength(PublicationJob.ErrorMessageMax);
        foreach (var column in new[]
                 {
                     nameof(PublicationJob.PublishAtUtc), nameof(PublicationJob.LocalPublishAt), nameof(PublicationJob.NextActionAtUtc),
                     nameof(PublicationJob.LockedAtUtc), nameof(PublicationJob.LeaseExpiresAtUtc), nameof(PublicationJob.DispatchStartedAtUtc),
                     nameof(PublicationJob.CompletedAtUtc), nameof(PublicationJob.CreatedAtUtc), nameof(PublicationJob.CanceledAtUtc),
                     nameof(PublicationJob.UpdatedAtUtc), nameof(PublicationJob.MediaContainerCreatedAtUtc),
                 })
            builder.Property(column).HasColumnType("datetime2(3)");
        builder.Property(j => j.RowVersion).IsRowVersion();
        builder.Ignore(j => j.IsInFlight);
        builder.Ignore(j => j.CanCancel);
        builder.Ignore(j => j.CanReschedule);
        builder.IsEnumString<PublicationJob, SocialPlatform>(j => j.Platform, "Platform");
        builder.IsEnumString<PublicationJob, ExecutionMode>(j => j.ExecutionMode, "ExecutionMode");
        builder.IsEnumString<PublicationJob, DeliveryState>(j => j.DeliveryState, "DeliveryState");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_PublicationJobs_ExecutableMode", "[ExecutionMode] <> 'NOT_CONFIGURED'");
            // Simulation is never reported as a real publication, and never carries provider evidence.
            t.HasCheckConstraint("CK_PublicationJobs_SimulationIsHonest",
                "NOT ([ExecutionMode] = 'SIMULATED' AND [DeliveryState] = 'PUBLISHED') AND NOT ([ExecutionMode] <> 'SIMULATED' AND [DeliveryState] = 'SIMULATED') " +
                "AND ([ExecutionMode] <> 'SIMULATED' OR ([ExternalPostId] IS NULL AND [ExternalPostUrl] IS NULL))");
            t.HasCheckConstraint("CK_PublicationJobs_LocalTime",
                "([LocalPublishAt] IS NULL AND [TimeZoneId] IS NULL AND [UtcOffsetMinutes] IS NULL) " +
                "OR ([LocalPublishAt] IS NOT NULL AND [TimeZoneId] IS NOT NULL AND [UtcOffsetMinutes] IS NOT NULL)");
            // A real publication is only ever recorded after the dispatch checkpoint was saved.
            t.HasCheckConstraint("CK_PublicationJobs_PublishedWasDispatched", "[DeliveryState] <> 'PUBLISHED' OR [DispatchStartedAtUtc] IS NOT NULL");
            t.HasCheckConstraint("CK_PublicationJobs_Attempts", "[AttemptCount] >= 0 AND [MaxAttempts] >= 1 AND [AttemptCount] <= [MaxAttempts]");
        });

        builder.HasOne<Organization>().WithMany().HasForeignKey(j => j.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        // The approval of exactly this version of this post, in this workspace.
        builder.HasOne<ReviewRequest>().WithMany()
            .HasForeignKey(j => new { j.OrganizationId, j.PostId, j.PostVersionId, j.ReviewRequestId })
            .HasPrincipalKey(r => new { r.OrganizationId, r.PostId, r.PostVersionId, r.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SocialAccount>().WithMany()
            .HasForeignKey(j => new { j.OrganizationId, j.SocialAccountId }).HasPrincipalKey(a => new { a.OrganizationId, a.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMemberReference(j => new { j.OrganizationId, j.CreatedByUserId });
        builder.HasMemberReference(j => new { j.OrganizationId, j.CanceledByUserId });
        builder.HasAlternateKey(j => new { j.OrganizationId, j.Id });

        // One lifetime publication intent per workspace, exact version and destination (PRD §27): double clicks and
        // concurrent requests cannot create a second job.
        builder.HasIndex(j => new { j.OrganizationId, j.PostVersionId, j.SocialAccountId }).IsUnique().HasDatabaseName("UX_PublicationJobs_LifetimeIntent");
        // Worker: due jobs by state and next action time.
        builder.HasIndex(j => new { j.DeliveryState, j.NextActionAtUtc });
        // Upcoming/calendar view per workspace.
        builder.HasIndex(j => new { j.OrganizationId, j.PublishAtUtc });
    }
}
