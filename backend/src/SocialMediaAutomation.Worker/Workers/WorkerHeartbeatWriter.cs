using System.Collections.Concurrent;
using SocialMediaAutomation.Application.Operations;
using SocialMediaAutomation.Domain.Entities;

namespace SocialMediaAutomation.Worker.Workers;

/// <summary>Records that a worker loop is alive, at most every 30 seconds. A failed write is logged, never fatal.</summary>
public sealed class WorkerHeartbeatWriter(IServiceScopeFactory scopes, ILogger<WorkerHeartbeatWriter> logger)
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private static readonly string? Version = typeof(WorkerHeartbeatWriter).Assembly.GetName().Version?.ToString();
    // Shared by the AI and publishing loops, which run concurrently.
    private readonly ConcurrentDictionary<string, (DateTime Started, DateTime Last)> beats = new();

    public async Task BeatAsync(string workerId, WorkerKind kind, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var (started, last) = beats.TryGetValue(workerId, out var known) ? known : (now, DateTime.MinValue);
        if (now - last < Interval)
            return;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IWorkerHeartbeats>().BeatAsync(workerId, kind, started, cancellationToken, Version);
            beats[workerId] = (started, now);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Worker {WorkerId} could not record its heartbeat", workerId);
        }
    }
}
