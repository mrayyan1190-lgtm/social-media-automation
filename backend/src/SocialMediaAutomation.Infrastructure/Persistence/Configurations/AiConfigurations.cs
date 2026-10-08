using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Infrastructure.Persistence.Configurations;

internal sealed class AiProviderSettingsConfiguration : IEntityTypeConfiguration<AiProviderSettings>
{
    public void Configure(EntityTypeBuilder<AiProviderSettings> builder)
    {
        builder.ToTable("AiProviderSettings");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.BaseUrl).HasMaxLength(500).IsRequired();
        builder.Property(s => s.Model).HasMaxLength(AiProviderSettings.ModelMax).IsRequired();
        builder.Property(s => s.ProtectedApiKey).HasMaxLength(4000).IsRequired();
        builder.Property(s => s.ApiKeyHint).HasMaxLength(8).IsRequired();
        builder.Property(s => s.UpdatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(s => s.RowVersion).IsRowVersion();
        builder.IsEnumString<AiProviderSettings, AiProvider>(s => s.Provider, "Provider");
        builder.HasOne<Organization>().WithOne().HasForeignKey<AiProviderSettings>(s => s.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(s => s.OrganizationId).IsUnique();
        builder.HasMemberReference(s => new { s.OrganizationId, s.UpdatedByUserId });
    }
}

internal sealed class AiGenerationRequestConfiguration : IEntityTypeConfiguration<AiGenerationRequest>
{
    public void Configure(EntityTypeBuilder<AiGenerationRequest> builder)
    {
        builder.ToTable("AiGenerationRequests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.CustomInstruction).HasMaxLength(AiGenerationRequest.CustomInstructionMax);
        builder.Property(r => r.InputSnapshot).IsRequired();
        builder.AddJsonCheck("InputSnapshot");
        builder.Property(r => r.PromptVersion).HasMaxLength(50).IsRequired();
        builder.Property(r => r.SchemaVersion).HasMaxLength(50).IsRequired();
        builder.Property(r => r.Model).HasMaxLength(AiProviderSettings.ModelMax).IsRequired();
        builder.Property(r => r.LeasedBy).HasMaxLength(200);
        builder.Property(r => r.FailureCode).HasMaxLength(50);
        builder.Property(r => r.FailureMessage).HasMaxLength(1000);
        builder.Property(r => r.Warning).HasMaxLength(AiGenerationRequest.WarningMax);
        builder.IsEnumString<AiGenerationRequest, AiRequestStage>(r => r.Stage, "Stage");
        builder.Property(r => r.CorrelationId).HasMaxLength(SocialMediaAutomation.Domain.Common.CorrelationIds.MaxLength);
        foreach (var column in new[] { "NextAttemptAtUtc", "LeaseExpiresAtUtc", "CreatedAtUtc", "StartedAtUtc", "CompletedAtUtc" })
            builder.Property(column).HasColumnType("datetime2(3)");
        builder.Property(r => r.RowVersion).IsRowVersion();
        builder.Ignore(r => r.IsActive);
        builder.IsEnumString<AiGenerationRequest, AiRequestKind>(r => r.Kind, "Kind");
        builder.IsEnumString<AiGenerationRequest, AiRequestStatus>(r => r.Status, "Status");
        builder.IsEnumString<AiGenerationRequest, AiProvider>(r => r.Provider, "Provider");
        builder.IsEnumString<AiGenerationRequest, RewriteAction>(r => r.RewriteAction, "RewriteAction");
        builder.PrimitiveCollection(r => r.Platforms).HasColumnName("Platforms").IsRequired().ElementType(e => e.HasConversion<string>());
        builder.AddJsonCheck("Platforms");

        builder.HasOne<Post>().WithMany()
            .HasForeignKey(r => new { r.OrganizationId, r.PostId }).HasPrincipalKey(p => new { p.OrganizationId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMemberReference(r => new { r.OrganizationId, r.RequestedByUserId });
        builder.HasAlternateKey(r => new { r.OrganizationId, r.Id });

        // Worker claim: due pending work and expired leases (AGENTS.md §62).
        builder.HasIndex(r => new { r.Status, r.NextAttemptAtUtc });
        builder.HasIndex(r => new { r.OrganizationId, r.CreatedAtUtc });
    }
}

internal sealed class AiRequestOutputConfiguration : IEntityTypeConfiguration<AiRequestOutput>
{
    public void Configure(EntityTypeBuilder<AiRequestOutput> builder)
    {
        builder.ToTable("AiRequestOutputs");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.OutputJson).IsRequired();
        builder.AddJsonCheck("OutputJson");
        builder.Property(o => o.CreatedAtUtc).HasColumnType("datetime2(3)");
        builder.IsEnumString<AiRequestOutput, SocialPlatform>(o => o.Platform, "Platform");
        builder.HasOne<AiGenerationRequest>().WithMany()
            .HasForeignKey(o => new { o.OrganizationId, o.RequestId }).HasPrincipalKey(r => new { r.OrganizationId, r.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PostVersion>().WithMany()
            .HasForeignKey(o => new { o.OrganizationId, o.PostId, o.PostVersionId }).HasPrincipalKey(v => new { v.OrganizationId, v.PostId, v.Id })
            .OnDelete(DeleteBehavior.Restrict);
        // One output per request and platform variant (ARCHITECTURE.md §36).
        builder.HasIndex(o => new { o.RequestId, o.Platform }).IsUnique();
    }
}

internal sealed class AiCallLogConfiguration : IEntityTypeConfiguration<AiCallLog>
{
    public void Configure(EntityTypeBuilder<AiCallLog> builder)
    {
        builder.ToTable("AiCallLogs");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.Model).HasMaxLength(AiProviderSettings.ModelMax).IsRequired();
        builder.Property(l => l.PromptVersion).HasMaxLength(50).IsRequired();
        builder.Property(l => l.ErrorCode).HasMaxLength(50);
        builder.Property(l => l.EstimatedCostUsd).HasPrecision(12, 6);
        builder.Property(l => l.Diagnostic).HasMaxLength(AiCallLog.DiagnosticMax);
        builder.Property(l => l.CreatedAtUtc).HasColumnType("datetime2(3)");
        builder.IsEnumString<AiCallLog, AiCallPurpose>(l => l.Purpose, "Purpose");
        builder.IsEnumString<AiCallLog, AiProvider>(l => l.Provider, "Provider");
        builder.HasOne<AiGenerationRequest>().WithMany()
            .HasForeignKey(l => new { l.OrganizationId, l.RequestId }).HasPrincipalKey(r => new { r.OrganizationId, r.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ContentEvaluationConfiguration : IEntityTypeConfiguration<ContentEvaluation>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public void Configure(EntityTypeBuilder<ContentEvaluation> builder)
    {
        builder.ToTable("ContentEvaluations");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Summary).HasMaxLength(1000);
        builder.Property(e => e.EvaluatorModel).HasMaxLength(AiProviderSettings.ModelMax);
        builder.Property(e => e.PromptVersion).HasMaxLength(50);
        builder.Property(e => e.CreatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(e => e.Issues)
            .HasConversion(
                issues => JsonSerializer.Serialize(issues, Json),
                json => JsonSerializer.Deserialize<List<EvaluationIssue>>(json, Json) ?? new List<EvaluationIssue>(),
                new ValueComparer<List<EvaluationIssue>>((a, b) => a!.SequenceEqual(b!), list => list.Aggregate(0, (h, i) => HashCode.Combine(h, i)), list => list.ToList()))
            .HasColumnName("Issues")
            .IsRequired();
        builder.AddJsonCheck("Issues");

        builder.HasOne<PostVersion>().WithMany()
            .HasForeignKey(e => new { e.OrganizationId, e.PostId, e.PostVersionId }).HasPrincipalKey(v => new { v.OrganizationId, v.PostId, v.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AiGenerationRequest>().WithMany()
            .HasForeignKey(e => new { e.OrganizationId, e.RequestId }).HasPrincipalKey(r => new { r.OrganizationId, r.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.PostVersionId).IsUnique();
    }
}
