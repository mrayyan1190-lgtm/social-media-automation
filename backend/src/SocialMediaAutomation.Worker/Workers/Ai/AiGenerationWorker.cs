using Microsoft.Extensions.Options;
using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.AiGeneration.Services;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Worker.Options;

namespace SocialMediaAutomation.Worker.Workers.Ai;

/// <summary>
/// Polls SQL for due AI requests and processes them one at a time, each in its own DI scope. Correctness lives in the
/// database (claims, leases, statuses), so the worker can stop or restart at any time without losing work.
/// </summary>
public sealed class AiGenerationWorker(
    IServiceScopeFactory scopes, WorkerHeartbeatWriter heartbeat, IOptions<WorkersOptions> options, ISystemEventLog systemLog, ILogger<AiGenerationWorker> logger)
    : BackgroundService
{
    private readonly string workerId = $"{Environment.MachineName}:{Environment.ProcessId}:ai";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var idleDelay = TimeSpan.FromSeconds(Math.Max(1, options.Value.Ai.PollIntervalSeconds));
        logger.LogInformation("AI generation worker {WorkerId} started (poll interval {PollIntervalSeconds}s)", workerId, idleDelay.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = false;
            await heartbeat.BeatAsync(workerId, WorkerKind.AI, stoppingToken);
            try
            {
                (Guid RequestId, Guid LeaseToken)? claim;
                await using (var scope = scopes.CreateAsyncScope())
                    claim = await scope.ServiceProvider.GetRequiredService<AiGenerationProcessor>().ClaimNextAsync(workerId, stoppingToken);

                if (claim is { } claimed)
                {
                    processed = true;
                    using var _ = logger.BeginScope("AiGenerationRequestId:{AiGenerationRequestId}", claimed.RequestId);
                    logger.LogInformation("Processing AI request");
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<AiGenerationProcessor>().ProcessAsync(claimed.RequestId, claimed.LeaseToken, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // The request keeps its lease until expiry and is then reclaimed; never crash the loop.
                logger.LogError(ex, "AI worker iteration failed");
                await systemLog.RecordAsync(SystemEventLevels.Error, "AI Worker", "ITERATION_FAILED", $"{ex.GetType().Name}: {ex.Message}", cancellationToken: CancellationToken.None);
            }

            if (!processed)
                await Task.Delay(idleDelay, stoppingToken).ContinueWith(_ => { }, CancellationToken.None);
        }
    }
}
