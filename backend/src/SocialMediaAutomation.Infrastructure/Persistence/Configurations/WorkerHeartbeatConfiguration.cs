using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SocialMediaAutomation.Domain.Entities;

namespace SocialMediaAutomation.Infrastructure.Persistence.Configurations;

internal sealed class WorkerHeartbeatConfiguration : IEntityTypeConfiguration<WorkerHeartbeat>
{
    public void Configure(EntityTypeBuilder<WorkerHeartbeat> builder)
    {
        builder.ToTable("WorkerHeartbeats");
        builder.HasKey(w => w.WorkerId);
        builder.Property(w => w.WorkerId).HasMaxLength(200);
        builder.Property(w => w.StartedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(w => w.LastSeenAtUtc).HasColumnType("datetime2(3)");
        builder.IsEnumString<WorkerHeartbeat, WorkerKind>(w => w.Kind, "Kind");
        builder.Property(w => w.Version).HasMaxLength(50);
        builder.HasIndex(w => w.LastSeenAtUtc);
    }
}
