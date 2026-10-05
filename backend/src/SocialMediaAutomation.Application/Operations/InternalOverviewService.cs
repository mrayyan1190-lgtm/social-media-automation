using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.Common.Authorization;
using SocialMediaAutomation.Application.Platform;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Application.Operations;

// ---- GET /api/internal/overview: platform operations dashboard (counts and operational metadata, never customer content) ----

public static class AlertSeverity
{
    public const string Critical = "CRITICAL";
    public const string Warning = "WARNING";
    public const string Info = "INFO";
}

public sealed record OverviewSummaryDto(int Workspaces, int ActiveUsers, int AiRequestsToday, int PublishedToday, int FailedPublicationsToday, int ReconciliationRequired);

public sealed record OverviewAlertDto(string Severity, string Title, string Detail, string Component, DateTime AtUtc, string ActionLabel, string ActionRoute);

public sealed record AiQueueSummaryDto(int Pending, int Processing, int RetryPending, int FailedToday, DateTime? OldestPendingAtUtc);

public sealed record PublishingQueueSummaryDto(
    int Scheduled, int Processing, int WaitingMedia, int RetryPending, int ActionRequired, int ReconciliationRequired, DateTime? OldestDueAtUtc);

public sealed record OverviewQueuesDto(AiQueueSummaryDto Ai, PublishingQueueSummaryDto Publishing);

/// <summary>Tokens and cost are null when no call recorded them (no invented numbers).</summary>
public sealed record AiOperationsDto(
    int RequestsToday, int CompletedToday, int FailedToday, double? SuccessRate, int? AverageResponseMs, long? TokensToday, decimal? EstimatedCostUsdToday);

public sealed record PlatformPublishingDto(SocialPlatform Platform, int PublishedToday, int FailedToday);

public sealed record PublishingOperationsDto(
    int ScheduledNextHour, int PublishedToday, int SimulatedToday, int FailedToday, int ActionRequired, int ReconciliationRequired,
    IReadOnlyList<PlatformPublishingDto> ByPlatform);

public sealed record ReconciliationItemDto(Guid JobId, string WorkspaceName, SocialPlatform Platform, string? ErrorCode, string? Reason, DateTime LastAttemptAtUtc);

public sealed record WorkspaceRowDto(Guid Id, string Name, DateTime CreatedAtUtc, int Members, int Campaigns, OrganizationStatus Status);

public sealed record WorkspaceOverviewDto(int Total, int Active, int Suspended, int NewThisWeek, IReadOnlyList<WorkspaceRowDto> Latest);

public sealed record UserRowDto(string DisplayName, string Email, DateTime CreatedAtUtc);

/// <summary>Customer accounts only (system administrators are not counted).</summary>
public sealed record UserOverviewDto(int Total, int Active, int NewThisWeek, int Disabled, IReadOnlyList<UserRowDto> RecentRegistrations);

/// <summary>Customer destinations per platform. Demo = simulated destinations (never sent externally).</summary>
public sealed record ConnectionHealthDto(SocialPlatform Platform, int Connected, int NeedsReauthorization, int Disconnected, int Demo);

public sealed record AuditItemDto(Guid Id, string? ActorEmail, string Action, string WorkspaceName, DateTime AtUtc);

/// <summary>System status row on the overview: one of HEALTHY, DEGRADED, UNAVAILABLE, NOT_CONFIGURED, NOT_MONITORED.</summary>
public sealed record OverviewStatusDto(string Name, string Status, string? Detail);

public sealed record OverviewOperationsDto(
    int AiPending, int AiProcessing, int AiFailedToday, int PublishingScheduled, int PublishingProcessing, int PublishingFailedToday,
    int PublishingNeedsAttention);

/// <summary>GET /api/internal/overview: deliberately small. Details live one click deeper.</summary>
public sealed record InternalOverviewDto(
    string Environment,
    DateTime GeneratedAtUtc,
    OverviewSummaryDto Summary,
    IReadOnlyList<OverviewAlertDto> Attention,
    IReadOnlyList<OverviewStatusDto> Status,
    OverviewOperationsDto Operations,
    IReadOnlyList<AuditLogItemDto> RecentAdminActivity);

/// <summary>LLM calls in the last hour (from AiCallLogs).</summary>
public sealed record LlmActivity(int Successes, int Failures, int? AverageLatencyMs, DateTime? LastSuccessAtUtc);

/// <summary>Live/test-account publication attempts for one platform (simulation never counts as provider traffic).</summary>
public sealed record ProviderActivity(SocialPlatform Platform, DateTime? LastSuccessAtUtc, int SuccessesLast24Hours, int FailuresLast24Hours);

/// <summary>Raw platform aggregates, read in one place by Infrastructure.</summary>
public sealed record PlatformStats(
    int DatabaseLatencyMs,
    WorkspaceOverviewDto Workspaces,
    UserOverviewDto Users,
    OverviewQueuesDto Queues,
    AiOperationsDto Ai,
    PublishingOperationsDto Publishing,
    IReadOnlyList<ReconciliationItemDto> Reconciliation,
    IReadOnlyList<ConnectionHealthDto> Connections,
    IReadOnlyList<AuditItemDto> RecentAudit,
    LlmActivity Llm,
    IReadOnlyList<ProviderActivity> Providers,
    IReadOnlyDictionary<string, int> CurrentJobsByWorker);

public interface IPlatformStatsQueries
{
    Task<PlatformStats> GetAsync(DateTime utcNow, CancellationToken cancellationToken);
}

/// <summary>
/// Read-only internal overview: summary counts, operational problems that need a person, compact system status and
/// recent operator actions. Health comes from <see cref="SystemHealthService"/> (real probes, heartbeats and traffic).
/// </summary>
public sealed class InternalOverviewService(
    SystemAdminAuthorizer systemAdmins,
    IPlatformStatsQueries stats,
    SystemHealthService health,
    IInternalQueries queries,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan QueueDelayWarning = TimeSpan.FromMinutes(5);

    private static readonly string[] Worst =
        [HealthStatusCodes.Unavailable, HealthStatusCodes.Degraded, HealthStatusCodes.NotConfigured, HealthStatusCodes.NotMonitored, HealthStatusCodes.Healthy];

    public async Task<InternalOverviewDto> GetAsync(string environment, CancellationToken cancellationToken)
    {
        await systemAdmins.RequireAsync(cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var s = await stats.GetAsync(now, cancellationToken);
        var components = await health.CheckAsync(cancellationToken);
        var recentAdmin = (await queries.AuditLogAsync(new LogQueryDto(null, null, null, null, 25), null, null, cancellationToken))
            .Where(a => a.Scope == "PLATFORM").Take(5).ToList();

        return new InternalOverviewDto(
            environment.ToUpperInvariant(),
            now,
            new OverviewSummaryDto(s.Workspaces.Total, s.Users.Total, s.Ai.RequestsToday, s.Publishing.PublishedToday, s.Publishing.FailedToday,
                s.Publishing.ReconciliationRequired),
            Attention(s, components, now),
            [
                Row("API", components, "API"),
                Row("Database", components, "SQL Server"),
                Row("Storage", components, "Object Storage"),
                Row("AI / LLM", components, "AI Provider", "AI Worker"),
                Row("Publishing", components, "Publishing Worker"),
                Row("LinkedIn", components, "LinkedIn API"),
                Row("Meta", components, "Meta API"),
            ],
            new OverviewOperationsDto(s.Queues.Ai.Pending, s.Queues.Ai.Processing, s.Ai.FailedToday, s.Queues.Publishing.Scheduled,
                s.Queues.Publishing.Processing, s.Publishing.FailedToday, s.Publishing.ActionRequired + s.Publishing.ReconciliationRequired),
            recentAdmin);
    }

    /// <summary>A status row combining several components shows the worst of them.</summary>
    private static OverviewStatusDto Row(string name, IReadOnlyList<SystemHealthComponentDto> components, params string[] prefixes)
    {
        var worst = components.Where(c => prefixes.Any(p => c.Component.StartsWith(p, StringComparison.Ordinal)))
            .OrderBy(c => Array.IndexOf(Worst, c.Status)).FirstOrDefault();
        return new OverviewStatusDto(name, worst?.Status ?? HealthStatusCodes.NotMonitored, worst?.LastError);
    }

    private static List<OverviewAlertDto> Attention(PlatformStats s, IReadOnlyList<SystemHealthComponentDto> components, DateTime now)
    {
        var alerts = new List<OverviewAlertDto>();
        foreach (var c in components)
        {
            var (label, route) = c.Component switch
            {
                "AI Worker" or "Publishing Worker" => ("View Workers", "/internal/workers"),
                var n when n.StartsWith("AI Provider", StringComparison.Ordinal) => ("AI / LLM", "/internal/ai"),
                "LinkedIn API" or "Meta API" => ("Social Providers", "/internal/social-providers"),
                _ => ("System Health", "/internal/system-health"),
            };
            var provider = c.Component is "LinkedIn API" or "Meta API";
            if (c.Status == HealthStatusCodes.Unavailable)
                alerts.Add(new(AlertSeverity.Critical, $"{c.Component} unavailable", c.LastError ?? "", c.Component, c.LastCheckAtUtc ?? now, label, route));
            else if (c.Status == HealthStatusCodes.Degraded)
                alerts.Add(new(AlertSeverity.Warning, $"{c.Component} degraded", c.LastError ?? "", c.Component, c.LastCheckAtUtc ?? now, label, route));
            else if (c.Status == HealthStatusCodes.NotConfigured)
                alerts.Add(new(provider ? AlertSeverity.Info : AlertSeverity.Critical, $"{c.Component} not configured", c.LastError ?? "", c.Component, now, label, route));
        }

        if (s.Publishing.ReconciliationRequired > 0)
            alerts.Add(new(AlertSeverity.Warning, $"{s.Publishing.ReconciliationRequired} publication(s) need reconciliation",
                "The external outcome could not be confirmed.", "Publishing", now, "Open Reconciliation", "/internal/reconciliation"));
        if (s.Publishing.FailedToday > 0)
            alerts.Add(new(AlertSeverity.Warning, $"{s.Publishing.FailedToday} publication(s) failed today", "Reported by a provider or validation.",
                "Publishing", now, "View Publications", "/internal/publications?status=FAILED"));
        if (s.Queues.Ai.OldestPendingAtUtc is { } aiOldest && now - aiOldest > QueueDelayWarning)
            alerts.Add(new(AlertSeverity.Warning, "AI queue backlog", $"{s.Queues.Ai.Pending} request(s) waiting for over {QueueDelayWarning.TotalMinutes:0} minutes.",
                "AI queue", aiOldest, "Open Queues", "/internal/queues"));
        if (s.Queues.Publishing.OldestDueAtUtc is { } pubOldest && now - pubOldest > QueueDelayWarning)
            alerts.Add(new(AlertSeverity.Warning, "Publishing queue backlog", $"Due posts have waited over {QueueDelayWarning.TotalMinutes:0} minutes for a worker.",
                "Publishing queue", pubOldest, "Open Queues", "/internal/queues?tab=publishing"));

        string[] order = [AlertSeverity.Critical, AlertSeverity.Warning, AlertSeverity.Info];
        return alerts.OrderBy(a => Array.IndexOf(order, a.Severity)).ToList();
    }
}
