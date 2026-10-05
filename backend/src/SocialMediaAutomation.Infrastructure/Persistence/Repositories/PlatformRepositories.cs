using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Domain.Entities;

namespace SocialMediaAutomation.Infrastructure.Persistence.Repositories;

internal sealed class PlatformRepository(CoreDbContext db) : IPlatformRepository
{
    public Task<PlatformAiConfiguration?> GetAiConfigurationAsync(CancellationToken cancellationToken) =>
        db.PlatformAiConfigurations.SingleOrDefaultAsync(c => c.Id == PlatformAiConfiguration.SingletonId, cancellationToken);

    public Task<SocialProviderConfiguration?> GetProviderAsync(PlatformProvider provider, CancellationToken cancellationToken) =>
        db.SocialProviderConfigurations.SingleOrDefaultAsync(c => c.Provider == provider, cancellationToken);

    public async Task<IReadOnlyList<SocialProviderConfiguration>> ListProvidersAsync(CancellationToken cancellationToken) =>
        await db.SocialProviderConfigurations.AsNoTracking().ToListAsync(cancellationToken);

    public void Add(PlatformAiConfiguration configuration) => db.PlatformAiConfigurations.Add(configuration);

    public void Add(SocialProviderConfiguration configuration) => db.SocialProviderConfigurations.Add(configuration);

    public void Add(PlatformAuditLog entry) => db.PlatformAuditLogs.Add(entry);

    public void Add(SignInEvent signIn) => db.SignInEvents.Add(signIn);
}

/// <summary>
/// MVP secret store: values encrypted with the data-protection key ring and kept in PlatformSecrets. Writes join the
/// caller's unit of work, so a configuration change and its secret commit together.
/// ponytail: replace with an Azure Key Vault (or similar) implementation of ISecretStore for production deployments.
/// </summary>
internal sealed class DatabaseSecretStore(CoreDbContext db, ISecretProtector protector, TimeProvider timeProvider) : ISecretStore
{
    public async Task SetAsync(string name, string value, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var existing = await db.PlatformSecrets.SingleOrDefaultAsync(s => s.Name == name, cancellationToken);
        if (existing is null)
            db.PlatformSecrets.Add(PlatformSecret.Create(name, protector.Protect(value), now));
        else
            existing.Replace(protector.Protect(value), now);
    }

    public async Task<string?> GetAsync(string name, CancellationToken cancellationToken)
    {
        var stored = await db.PlatformSecrets.AsNoTracking().SingleOrDefaultAsync(s => s.Name == name, cancellationToken);
        return stored is null ? null : protector.Unprotect(stored.ProtectedValue);
    }
}

/// <summary>Writes system events in their own scope so they never interfere with (or roll back with) the caller's work.</summary>
internal sealed class SystemEventWriter(IServiceScopeFactory scopes, TimeProvider timeProvider, ILogger<SystemEventWriter> logger) : ISystemEventLog
{
    public async Task RecordAsync(string level, string service, string eventName, string message, string? correlationId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            db.SystemEvents.Add(SystemEvent.Record(level, service, eventName, message, correlationId, timeProvider.GetUtcNow().UtcDateTime));
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Never let logging break the operation that is being logged.
            logger.LogWarning(ex, "Could not record system event {EventName} from {Service}", eventName, service);
        }
    }
}
