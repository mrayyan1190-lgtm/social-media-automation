using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SocialMediaAutomation.Infrastructure.Persistence;

public sealed class DatabaseHealthCheck(CoreDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("SQL Server database is not reachable.");
}
