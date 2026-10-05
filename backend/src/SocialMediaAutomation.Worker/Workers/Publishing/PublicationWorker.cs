using Microsoft.Extensions.Options;
using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.Publications.Services;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Worker.Options;

namespace SocialMediaAutomation.Worker.Workers.Publishing;

/// <summary>
/// Polls SQL for due publication jobs. Each pass settles abandoned leases, claims a small batch in one short statement,
/// then processes each claimed job in its own DI scope. All state lives in the database, so the worker (or the browser
/// that scheduled the job) can stop at any time: expired pre-dispatch leases are reclaimed, post-dispatch ones reconciled.
/// </summary>
public sealed class PublicationWorker(
    IServiceScopeFactory scopes, WorkerHeartbeatWriter heartbeat, IOptions<WorkersOptions> options, ISystemEventLog systemLog, ILogger<PublicationWorker> logger)
    : BackgroundService
{
    private readonly string workerId = $"{Environment.MachineName}:{Environment.ProcessId}:publishing";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var idleDelay = TimeSpan.FromSeconds(Math.Max(1, options.Value.Publishing.PollIntervalSeconds));
        logger.LogInformation("Publication worker {WorkerId} started (poll interval {PollIntervalSeconds}s)", workerId, idleDelay.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            var claimed = 0;
            await heartbeat.BeatAsync(workerId, WorkerKind.PUBLISHING, stoppingToken);
            try
            {
                IReadOnlyList<(Guid JobId, Guid LeaseToken)> claims;
                await using (var scope = scopes.CreateAsyncScope())
                {
                    var processor = scope.ServiceProvider.GetRequiredService<PublicationProcessor>();
                    var settled = await processor.SweepAbandonedAsync(stoppingToken);
                    if (settled > 0)
                    {
                        logger.LogWarning("Settled {Count} publication job(s) whose worker stopped responding", settled);
                        await systemLog.RecordAsync(SystemEventLevels.Warning, "Publishing Worker", "ABANDONED_JOBS_SETTLED",
                            $"{settled} job(s) whose worker stopped responding were reclaimed, failed or sent to reconciliation.", cancellationToken: CancellationToken.None);
                    }
                    claims = await processor.ClaimAsync(workerId, stoppingToken);
                }
                claimed = claims.Count;

                foreach (var (jobId, leaseToken) in claims)
                {
                    using var _ = logger.BeginScope("PublicationJobId:{PublicationJobId}", jobId);
                    logger.LogInformation("Processing publication job");
                    try
                    {
                        await using var scope = scopes.CreateAsyncScope();
                        await scope.ServiceProvider.GetRequiredService<PublicationProcessor>().ProcessAsync(jobId, leaseToken, workerId, stoppingToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // The job keeps its lease until expiry; then it is reclaimed (before dispatch) or reconciled (after).
                        logger.LogError(ex, "Publication job processing failed");
                        await systemLog.RecordAsync(SystemEventLevels.Error, "Publishing Worker", "JOB_PROCESSING_FAILED",
                            $"Job {jobId}: {ex.GetType().Name}: {ex.Message}", cancellationToken: CancellationToken.None);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Publication worker iteration failed");
                await systemLog.RecordAsync(SystemEventLevels.Error, "Publishing Worker", "ITERATION_FAILED", $"{ex.GetType().Name}: {ex.Message}", cancellationToken: CancellationToken.None);
            }

            if (claimed == 0)
                await Task.Delay(idleDelay, stoppingToken).ContinueWith(_ => { }, CancellationToken.None);
        }
    }
}
