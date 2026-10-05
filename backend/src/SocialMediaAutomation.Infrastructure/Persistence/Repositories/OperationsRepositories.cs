using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SocialMediaAutomation.Application.Operations;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Infrastructure.Persistence.Repositories;

internal sealed class WorkerHeartbeats(CoreDbContext db) : IWorkerHeartbeats
{
    // One statement, so concurrent workers never race into a duplicate-key error.
    private const string UpsertSql = """
        MERGE WorkerHeartbeats WITH (HOLDLOCK) AS target
        USING (SELECT {0} AS WorkerId) AS source ON target.WorkerId = source.WorkerId
        WHEN MATCHED THEN UPDATE SET LastSeenAtUtc = {3}, Kind = {1}, StartedAtUtc = {2}, Version = {4}
        WHEN NOT MATCHED THEN INSERT (WorkerId, Kind, StartedAtUtc, LastSeenAtUtc, Version) VALUES ({0}, {1}, {2}, {3}, {4});
        """;

    public Task BeatAsync(string workerId, WorkerKind kind, DateTime startedAtUtc, CancellationToken cancellationToken, string? version = null) =>
        db.Database.ExecuteSqlRawAsync(UpsertSql,
            [workerId.Length > 200 ? workerId[..200] : workerId, kind.ToString(), startedAtUtc, DateTime.UtcNow, (object?)version?[..Math.Min(version.Length, 50)] ?? DBNull.Value],
            cancellationToken);

    public async Task<IReadOnlyList<WorkerHeartbeat>> ListAsync(CancellationToken cancellationToken) =>
        await db.WorkerHeartbeats.AsNoTracking().OrderByDescending(w => w.LastSeenAtUtc).Take(50).ToListAsync(cancellationToken);
}

internal sealed class OperationsRepository(CoreDbContext db) : IOperationsRepository
{
    private static readonly DeliveryState[] Claimable = [DeliveryState.SCHEDULED, DeliveryState.RETRY_PENDING, DeliveryState.WAITING_MEDIA];

    public async Task<PublicationQueueDto> PublicationQueueAsync(Guid? organizationId, DateTime utcNow, CancellationToken cancellationToken)
    {
        var jobs = db.PublicationJobs.AsNoTracking().Where(j => organizationId == null || j.OrganizationId == organizationId);
        var due = jobs.Where(j => Claimable.Contains(j.DeliveryState) && j.NextActionAtUtc <= utcNow);
        return new PublicationQueueDto(
            await due.CountAsync(cancellationToken),
            await due.MinAsync(j => j.NextActionAtUtc, cancellationToken),
            await jobs.CountAsync(j => j.DeliveryState == DeliveryState.PROCESSING, cancellationToken),
            await jobs.CountAsync(j => j.DeliveryState == DeliveryState.PROCESSING && j.LeaseExpiresAtUtc < utcNow, cancellationToken),
            await jobs.CountAsync(j => j.DeliveryState == DeliveryState.WAITING_MEDIA, cancellationToken),
            await jobs.CountAsync(j => j.DeliveryState == DeliveryState.RETRY_PENDING, cancellationToken),
            await jobs.CountAsync(j => j.DeliveryState == DeliveryState.RETRY_PENDING && j.LastErrorCode == "THROTTLED", cancellationToken),
            await jobs.CountAsync(j => j.DeliveryState == DeliveryState.ACTION_REQUIRED, cancellationToken),
            await jobs.CountAsync(j => j.DeliveryState == DeliveryState.RECONCILIATION_REQUIRED, cancellationToken));
    }

    public async Task<AiQueueDto> AiQueueAsync(Guid? organizationId, DateTime utcNow, CancellationToken cancellationToken)
    {
        var requests = db.AiGenerationRequests.AsNoTracking().Where(r => organizationId == null || r.OrganizationId == organizationId);
        var due = requests.Where(r => r.Status == AiRequestStatus.PENDING && r.NextAttemptAtUtc <= utcNow);
        return new AiQueueDto(
            await due.CountAsync(cancellationToken),
            await due.MinAsync(r => (DateTime?)r.NextAttemptAtUtc, cancellationToken),
            await requests.CountAsync(r => r.Status == AiRequestStatus.PROCESSING, cancellationToken),
            await requests.CountAsync(r => r.Status == AiRequestStatus.PROCESSING && r.LeaseExpiresAtUtc < utcNow, cancellationToken),
            await requests.CountAsync(r => r.Status == AiRequestStatus.FAILED && r.CompletedAtUtc > utcNow.AddHours(-24), cancellationToken));
    }
}

/// <summary>Readiness: Degraded when no publishing worker has reported recently (scheduled posts would not go out).</summary>
public sealed class WorkerHeartbeatHealthCheck(CoreDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow - WorkerHeartbeat.StaleAfter;
        var alive = await db.WorkerHeartbeats.AsNoTracking()
            .Where(w => w.LastSeenAtUtc >= cutoff).Select(w => w.Kind).Distinct().ToListAsync(cancellationToken);
        var missing = Enum.GetValues<WorkerKind>().Except(alive).ToList();
        return missing.Count == 0
            ? HealthCheckResult.Healthy("AI and publishing workers are reporting.")
            : HealthCheckResult.Degraded($"No recent heartbeat from: {string.Join(", ", missing)} worker(s).");
    }
}
