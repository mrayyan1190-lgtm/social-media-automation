using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SocialMediaAutomation.Domain.Entities;

namespace SocialMediaAutomation.Infrastructure.Persistence.Configurations;

internal sealed class PublicationAttemptConfiguration : IEntityTypeConfiguration<PublicationAttempt>
{
    public void Configure(EntityTypeBuilder<PublicationAttempt> builder)
    {
        builder.ToTable("PublicationAttempts");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.WorkerId).HasMaxLength(200).IsRequired();
        builder.Property(a => a.ErrorCode).HasMaxLength(100);
        builder.Property(a => a.ErrorMessage).HasMaxLength(PublicationJob.ErrorMessageMax);
        builder.Property(a => a.ProviderRequestId).HasMaxLength(200);
        builder.Property(a => a.ExternalId).HasMaxLength(200);
        builder.Property(a => a.Detail).HasMaxLength(PublicationAttempt.DetailMax);
        builder.Property(a => a.StartedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(a => a.FinishedAtUtc).HasColumnType("datetime2(3)");
        builder.IsEnumString<PublicationAttempt, PublicationAttemptStep>(a => a.Step, "Step");
        builder.IsEnumString<PublicationAttempt, DeliveryState>(a => a.ResultState, "ResultState");
        builder.IsEnumString<PublicationAttempt, PublishErrorCategory>(a => a.ErrorCategory, "ErrorCategory");
        // Same-workspace link to the job.
        builder.HasOne<PublicationJob>().WithMany()
            .HasForeignKey(a => new { a.OrganizationId, a.PublicationJobId }).HasPrincipalKey(j => new { j.OrganizationId, j.Id })
            .OnDelete(DeleteBehavior.Restrict);
        // Attempt history per job, in order (also finds the open attempt).
        builder.HasIndex(a => new { a.PublicationJobId, a.StartedAtUtc });
    }
}
