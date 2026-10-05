using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SocialMediaAutomation.Application.Abstractions;

namespace SocialMediaAutomation.Infrastructure.Persistence.Repositories;

/// <summary>
/// SQL Server claim for publication jobs: one auto-committed UPDATE. READPAST skips rows another worker is claiming,
/// UPDLOCK stops two workers taking the same row, each row gets its own lease token, and no lock outlives the statement,
/// so nothing is held while providers are called (AGENTS.md §40-41).
/// Claimable: due SCHEDULED/RETRY_PENDING/WAITING_MEDIA jobs, and PROCESSING jobs whose lease expired before the
/// dispatch checkpoint (or that are simulated, which has no external side effect). Expired jobs past the dispatch
/// checkpoint are never reclaimed; the processor's sweep moves them to RECONCILIATION_REQUIRED.
/// </summary>
internal sealed class PublicationWorkQueue(CoreDbContext db, TimeProvider timeProvider) : IPublicationWorkQueue
{
    private const string ClaimSql = """
        WITH due AS (
            SELECT TOP (@batch) *
            FROM PublicationJobs WITH (ROWLOCK, UPDLOCK, READPAST)
            WHERE (DeliveryState IN ('SCHEDULED', 'RETRY_PENDING') AND NextActionAtUtc <= @now AND AttemptCount < MaxAttempts)
               OR (DeliveryState = 'WAITING_MEDIA' AND NextActionAtUtc <= @now)
               OR (DeliveryState = 'PROCESSING' AND LeaseExpiresAtUtc < @now
                   AND (DispatchStartedAtUtc IS NULL OR ExecutionMode = 'SIMULATED')
                   AND (ProviderContainerId IS NOT NULL OR AttemptCount < MaxAttempts))
            ORDER BY NextActionAtUtc)
        UPDATE due
        SET AttemptCount = AttemptCount + CASE WHEN DeliveryState = 'WAITING_MEDIA' OR ProviderContainerId IS NOT NULL THEN 0 ELSE 1 END,
            DeliveryState = 'PROCESSING',
            DispatchStartedAtUtc = CASE WHEN ExecutionMode = 'SIMULATED' THEN NULL ELSE DispatchStartedAtUtc END,
            LockedBy = @worker, LockedAtUtc = @now, LeaseExpiresAtUtc = @until, LeaseToken = NEWID(), UpdatedAtUtc = @now
        OUTPUT inserted.Id, inserted.LeaseToken;
        """;

    public async Task<IReadOnlyList<(Guid JobId, Guid LeaseToken)>> ClaimDueAsync(
        string workerId, int batchSize, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var connection = (SqlConnection)db.Database.GetDbConnection();
        var opened = connection.State != ConnectionState.Open;
        if (opened)
            await connection.OpenAsync(cancellationToken);
        try
        {
            await using var claim = new SqlCommand(ClaimSql, connection);
            claim.Parameters.AddWithValue("@batch", batchSize);
            claim.Parameters.AddWithValue("@now", now);
            claim.Parameters.AddWithValue("@until", now.Add(leaseDuration));
            claim.Parameters.AddWithValue("@worker", workerId);
            var claimed = new List<(Guid, Guid)>();
            await using var reader = await claim.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                claimed.Add((reader.GetGuid(0), reader.GetGuid(1)));
            return claimed;
        }
        finally
        {
            if (opened)
                await connection.CloseAsync();
        }
    }
}
