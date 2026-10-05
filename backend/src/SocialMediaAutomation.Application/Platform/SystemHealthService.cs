using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.AiGeneration.Services;
using SocialMediaAutomation.Application.Common.Authorization;
using SocialMediaAutomation.Application.Operations;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Application.Platform;

/// <summary>
/// One component's health. Status: HEALTHY, DEGRADED, UNAVAILABLE, NOT_CONFIGURED or NOT_MONITORED. Monitoring says how it
/// was measured (live probe, heartbeat, recent traffic); with no real signal the component is NOT_MONITORED, never HEALTHY.
/// </summary>
public sealed record SystemHealthComponentDto(
    string Component, string Status, int? LatencyMs, DateTime? LastCheckAtUtc, DateTime? LastSuccessAtUtc, string? LastError, string Monitoring);

/// <summary>
/// Live probes (API, SQL Server, object storage), worker heartbeats and recent provider traffic. Nothing here mutates
/// customer state; probe failures are written to the System log.
/// </summary>
public sealed class SystemHealthService(
    SystemAdminAuthorizer systemAdmins,
    IInternalQueries queries,
    IWorkerHeartbeats heartbeats,
    AiConnectionResolver aiResolver,
    ISocialOAuthGateway providers,
    ISystemEventLog systemLog,
    TimeProvider timeProvider,
    IStorageProbe? storageProbe = null)
{
    public static readonly TimeSpan OfflineAfter = TimeSpan.FromMinutes(10);
    private const int SlowDatabaseMs = 500;
    private const int SlowStorageMs = 1500;

    public async Task<IReadOnlyList<SystemHealthComponentDto>> GetAsync(CancellationToken cancellationToken)
    {
        await systemAdmins.RequireAsync(cancellationToken);
        return await CheckAsync(cancellationToken);
    }

    /// <summary>For callers that already verified the system administrator (the overview).</summary>
    internal async Task<IReadOnlyList<SystemHealthComponentDto>> CheckAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var workers = (await heartbeats.ListAsync(cancellationToken)).ToList();
        return
        [
            new("API", HealthStatusCodes.Healthy, null, now, now, null, "This request"),
            await DatabaseAsync(now, cancellationToken),
            await StorageAsync(now, cancellationToken),
            await AiAsync(now, cancellationToken),
            Worker("AI Worker", WorkerKind.AI, workers, now),
            Worker("Publishing Worker", WorkerKind.PUBLISHING, workers, now),
            await ProviderAsync("LinkedIn API", [SocialPlatform.LinkedIn], now, cancellationToken),
            await ProviderAsync("Meta API", [SocialPlatform.Facebook, SocialPlatform.Instagram], now, cancellationToken),
        ];
    }

    public static string WorkerStatus(DateTime lastSeenAtUtc, DateTime now) =>
        now - lastSeenAtUtc <= WorkerHeartbeat.StaleAfter ? "HEALTHY" : now - lastSeenAtUtc <= OfflineAfter ? "STALE" : "OFFLINE";

    private async Task<SystemHealthComponentDto> DatabaseAsync(DateTime now, CancellationToken cancellationToken)
    {
        try
        {
            var latency = await queries.DatabaseLatencyMsAsync(cancellationToken);
            return new("SQL Server", latency > SlowDatabaseMs ? HealthStatusCodes.Degraded : HealthStatusCodes.Healthy, latency, now, now,
                latency > SlowDatabaseMs ? "Slow response" : null, "Live query");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await systemLog.RecordAsync(SystemEventLevels.Critical, "Database", "HEALTH_PROBE_FAILED", ex.Message, cancellationToken: cancellationToken);
            return new("SQL Server", HealthStatusCodes.Unavailable, null, now, null, ex.Message, "Live query");
        }
    }

    private async Task<SystemHealthComponentDto> StorageAsync(DateTime now, CancellationToken cancellationToken)
    {
        if (storageProbe is null)
            return new("Object Storage", HealthStatusCodes.NotMonitored, null, null, null, null, "No probe for this storage provider");
        var started = timeProvider.GetTimestamp();
        try
        {
            await storageProbe.ProbeAsync(cancellationToken);
            var latency = (int)timeProvider.GetElapsedTime(started).TotalMilliseconds;
            return new("Object Storage", latency > SlowStorageMs ? HealthStatusCodes.Degraded : HealthStatusCodes.Healthy, latency, now, now,
                latency > SlowStorageMs ? "Slow response" : null, "Write, read and delete round trip");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await systemLog.RecordAsync(SystemEventLevels.Error, "Storage", "HEALTH_PROBE_FAILED", ex.Message, cancellationToken: cancellationToken);
            return new("Object Storage", HealthStatusCodes.Unavailable, null, now, null, ex.Message, "Write, read and delete round trip");
        }
    }

    private async Task<SystemHealthComponentDto> AiAsync(DateTime now, CancellationToken cancellationToken)
    {
        var config = await aiResolver.GetEffectiveAsync(cancellationToken);
        var activity = await queries.AiActivityAsync(now, cancellationToken);
        var name = config.Model is { Length: > 0 } ? $"AI Provider ({config.Provider} · {config.Model})" : "AI Provider";
        if (!config.IsUsable)
            return new(name, HealthStatusCodes.NotConfigured, null, null, activity.LastSuccessAtUtc,
                config.Source == "PLATFORM" && !config.Enabled ? "AI is disabled" : "No API key or model configured", "Configuration");
        var status = activity.CallsLastHour == 0 ? HealthStatusCodes.NotMonitored
            : activity.FailedCallsLastHour == activity.CallsLastHour ? HealthStatusCodes.Unavailable
            : activity.FailedCallsLastHour > 0 ? HealthStatusCodes.Degraded
            : HealthStatusCodes.Healthy;
        var lastCheck = new[] { activity.LastSuccessAtUtc, activity.LastFailureAtUtc }.Max();
        return new(name, status, activity.AverageLatencyMsLastHour, lastCheck, activity.LastSuccessAtUtc,
            activity.FailedCallsLastHour > 0 ? activity.LastFailure : null,
            activity.CallsLastHour == 0 ? "No calls in the last hour (use Test Connection)" : $"{activity.CallsLastHour} call(s) in the last hour");
    }

    private static SystemHealthComponentDto Worker(string name, WorkerKind kind, IReadOnlyList<WorkerHeartbeat> workers, DateTime now)
    {
        var latest = workers.Where(w => w.Kind == kind).MaxBy(w => w.LastSeenAtUtc);
        if (latest is null)
            return new(name, HealthStatusCodes.Unavailable, null, null, null, "Has never reported", "Heartbeat");
        return WorkerStatus(latest.LastSeenAtUtc, now) switch
        {
            "HEALTHY" => new(name, HealthStatusCodes.Healthy, null, latest.LastSeenAtUtc, latest.LastSeenAtUtc, null, "Heartbeat"),
            "STALE" => new(name, HealthStatusCodes.Degraded, null, latest.LastSeenAtUtc, latest.LastSeenAtUtc, "Heartbeat is late", "Heartbeat"),
            _ => new(name, HealthStatusCodes.Unavailable, null, latest.LastSeenAtUtc, latest.LastSeenAtUtc, "No heartbeat (worker offline)", "Heartbeat"),
        };
    }

    private async Task<SystemHealthComponentDto> ProviderAsync(string name, SocialPlatform[] platforms, DateTime now, CancellationToken cancellationToken)
    {
        if (!platforms.Any(providers.IsConfigured))
            return new(name, HealthStatusCodes.NotConfigured, null, null, null, "Provider app credentials are not configured", "Configuration");
        var activity = await queries.ProviderActivityAsync(platforms, now, cancellationToken);
        var total = activity.SuccessesLast24Hours + activity.FailuresLast24Hours;
        var status = total == 0 ? HealthStatusCodes.NotMonitored
            : activity.SuccessesLast24Hours == 0 ? HealthStatusCodes.Unavailable
            : activity.FailuresLast24Hours > 0 ? HealthStatusCodes.Degraded
            : HealthStatusCodes.Healthy;
        var lastCheck = new[] { activity.LastSuccessAtUtc, activity.LastFailureAtUtc }.Max();
        return new(name, status, null, lastCheck, activity.LastSuccessAtUtc, activity.FailuresLast24Hours > 0 ? activity.LastFailure : null,
            total == 0 ? "No live publishing in the last 24 hours" : $"{total} live request(s) in the last 24 hours");
    }
}
