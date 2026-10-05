using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SocialMediaAutomation.Domain.Common;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Infrastructure.Persistence.Configurations;

internal sealed class PlatformAiConfigurationConfiguration : IEntityTypeConfiguration<PlatformAiConfiguration>
{
    public void Configure(EntityTypeBuilder<PlatformAiConfiguration> builder)
    {
        builder.ToTable("PlatformAiConfigurations");
        builder.HasKey(c => c.Id);
        builder.IsEnumString<PlatformAiConfiguration, AiProvider>(c => c.Provider, "Provider");
        builder.Property(c => c.BaseUrl).HasMaxLength(Guard.UrlMaxLength);
        builder.Property(c => c.Model).HasMaxLength(AiProviderSettings.ModelMax).IsRequired();
        builder.Property(c => c.FallbackModel).HasMaxLength(AiProviderSettings.ModelMax);
        builder.Property(c => c.ApiKeySecretRef).HasMaxLength(200);
        builder.IsEnumString<PlatformAiConfiguration, AiProvider>(c => c.ImageProvider, "ImageProvider");
        builder.Property(c => c.ImageBaseUrl).HasMaxLength(Guard.UrlMaxLength);
        builder.Property(c => c.ImageModel).HasMaxLength(AiProviderSettings.ModelMax);
        builder.Property(c => c.ImageSize).HasMaxLength(20);
        builder.Property(c => c.ImageApiKeySecretRef).HasMaxLength(200);
        builder.Property(c => c.RowVersion).IsRowVersion();
    }
}

internal sealed class SocialProviderConfigurationConfiguration : IEntityTypeConfiguration<SocialProviderConfiguration>
{
    public void Configure(EntityTypeBuilder<SocialProviderConfiguration> builder)
    {
        builder.ToTable("SocialProviderConfigurations");
        builder.HasKey(c => c.Provider);
        builder.IsEnumString<SocialProviderConfiguration, PlatformProvider>(c => c.Provider, "Provider");
        builder.Property(c => c.ClientId).HasMaxLength(200).IsRequired();
        builder.Property(c => c.RedirectUri).HasMaxLength(Guard.UrlMaxLength).IsRequired();
        builder.Property(c => c.ApiVersion).HasMaxLength(20);
        builder.Property(c => c.ClientSecretRef).HasMaxLength(200);
        builder.Property(c => c.RowVersion).IsRowVersion();
    }
}

internal sealed class PlatformSecretConfiguration : IEntityTypeConfiguration<PlatformSecret>
{
    public void Configure(EntityTypeBuilder<PlatformSecret> builder)
    {
        builder.ToTable("PlatformSecrets");
        builder.HasKey(s => s.Name);
        builder.Property(s => s.Name).HasMaxLength(200);
        builder.Property(s => s.ProtectedValue).IsRequired();
    }
}

internal sealed class PlatformAuditLogConfiguration : IEntityTypeConfiguration<PlatformAuditLog>
{
    public void Configure(EntityTypeBuilder<PlatformAuditLog> builder)
    {
        builder.ToTable("PlatformAuditLogs");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Action).HasMaxLength(100).IsRequired();
        builder.Property(a => a.TargetType).HasMaxLength(50).IsRequired();
        builder.Property(a => a.TargetId).HasMaxLength(100).IsRequired();
        builder.Property(a => a.TargetLabel).HasMaxLength(200);
        builder.Property(a => a.Result).HasMaxLength(20).IsRequired();
        builder.Property(a => a.CorrelationId).HasMaxLength(CorrelationIds.MaxLength);
        builder.Property(a => a.MetadataJson).IsRequired();
        builder.AddJsonCheck("MetadataJson");
        builder.HasIndex(a => a.CreatedAtUtc);
    }
}

internal sealed class SignInEventConfiguration : IEntityTypeConfiguration<SignInEvent>
{
    public void Configure(EntityTypeBuilder<SignInEvent> builder)
    {
        builder.ToTable("SignInEvents");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Email).HasMaxLength(256).IsRequired();
        builder.Property(e => e.Area).HasMaxLength(20).IsRequired();
        builder.Property(e => e.EventType).HasMaxLength(50).IsRequired();
        builder.Property(e => e.IpAddress).HasMaxLength(64);
        builder.Property(e => e.UserAgent).HasMaxLength(300);
        builder.Property(e => e.CorrelationId).HasMaxLength(CorrelationIds.MaxLength);
        builder.HasIndex(e => e.CreatedAtUtc);
        builder.HasIndex(e => new { e.UserId, e.CreatedAtUtc });
    }
}

internal sealed class SystemEventConfiguration : IEntityTypeConfiguration<SystemEvent>
{
    public void Configure(EntityTypeBuilder<SystemEvent> builder)
    {
        builder.ToTable("SystemEvents");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Level).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Service).HasMaxLength(50).IsRequired();
        builder.Property(e => e.EventName).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Message).HasMaxLength(2000).IsRequired();
        builder.Property(e => e.CorrelationId).HasMaxLength(CorrelationIds.MaxLength);
        builder.HasIndex(e => e.CreatedAtUtc);
    }
}
