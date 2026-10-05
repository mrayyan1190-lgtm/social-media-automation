namespace SocialMediaAutomation.Domain.Entities;

public enum WorkerKind
{
    AI,
    PUBLISHING,
}

/// <summary>
/// Last sign of life of one worker process (operational data, not tenant data). Written periodically by each worker
/// loop so the API's readiness check and the operations view can tell whether background work is being picked up.
/// </summary>
public sealed class WorkerHeartbeat
{
    /// <summary>A worker that has not reported for this long is considered stopped.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);

    private WorkerHeartbeat() { }

    public string WorkerId { get; private set; } = null!;
    public WorkerKind Kind { get; private set; }
    public DateTime StartedAtUtc { get; private set; }
    public DateTime LastSeenAtUtc { get; private set; }

    /// <summary>Build of the worker process (assembly version), for spotting mixed deployments.</summary>
    public string? Version { get; private set; }

    public bool IsStale(DateTime utcNow) => utcNow - LastSeenAtUtc > StaleAfter;
}
