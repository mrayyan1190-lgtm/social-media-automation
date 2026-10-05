using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Domain.Constants;

namespace SocialMediaAutomation.Infrastructure.Persistence.Repositories;

/// <summary>
/// SQL Server claim for AI work. Each statement is a single auto-committed UPDATE: READPAST skips rows another worker
/// is claiming, UPDLOCK prevents two workers taking the same row, and no lock outlives the statement, so nothing is held
/// while the LLM is called (AGENTS.md §40-41).
/// </summary>
internal sealed class AiWorkQueue(CoreDbContext db, TimeProvider timeProvider) : IAiWorkQueue
{
    private const string FailAbandonedSql = """
        UPDATE AiGenerationRequests
        SET Status = 'FAILED', FailureCode = @lostCode, FailureMessage = 'The worker stopped responding on the final attempt.',
            CompletedAtUtc = @now, LeaseToken = NULL, LeasedBy = NULL, LeaseExpiresAtUtc = NULL
        WHERE Status = 'PROCESSING' AND LeaseExpiresAtUtc < @now AND AttemptCount >= MaxAttempts;
        """;

    private const string ClaimSql = """
        WITH next AS (
            SELECT TOP (1) *
            FROM AiGenerationRequests WITH (ROWLOCK, UPDLOCK, READPAST)
            WHERE (Status = 'PENDING' AND NextAttemptAtUtc <= @now)
               OR (Status = 'PROCESSING' AND LeaseExpiresAtUtc < @now AND AttemptCount < MaxAttempts)
            ORDER BY NextAttemptAtUtc)
        UPDATE next
        SET Status = 'PROCESSING', LeaseToken = @token, LeasedBy = @worker, LeaseExpiresAtUtc = @until,
            AttemptCount = AttemptCount + 1, StartedAtUtc = COALESCE(StartedAtUtc, @now)
        OUTPUT inserted.Id;
        """;

    public async Task<(Guid RequestId, Guid LeaseToken)?> TryClaimNextAsync(string workerId, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var token = Guid.NewGuid();
        var connection = (SqlConnection)db.Database.GetDbConnection();
        var opened = connection.State != ConnectionState.Open;
        if (opened)
            await connection.OpenAsync(cancellationToken);
        try
        {
            await using (var fail = new SqlCommand(FailAbandonedSql, connection))
            {
                fail.Parameters.AddWithValue("@now", now);
                fail.Parameters.AddWithValue("@lostCode", AiFailureCodes.WorkerLost);
                await fail.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var claim = new SqlCommand(ClaimSql, connection);
            claim.Parameters.AddWithValue("@now", now);
            claim.Parameters.AddWithValue("@token", token);
            claim.Parameters.AddWithValue("@worker", workerId);
            claim.Parameters.AddWithValue("@until", now.Add(leaseDuration));
            return await claim.ExecuteScalarAsync(cancellationToken) is Guid id ? (id, token) : null;
        }
        finally
        {
            if (opened)
                await connection.CloseAsync();
        }
    }
}
