using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using SocialMediaAutomation.Application.Operations;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Infrastructure.Persistence.Repositories;

/// <summary>
/// Cross-workspace aggregates for the internal dashboard (system administrators only; the service enforces that).
/// Only counts and operational metadata are projected: no post text, prompts, tokens or credentials.
/// "Today" is the current UTC day.
/// </summary>
internal sealed class PlatformStatsQueries(CoreDbContext db) : IPlatformStatsQueries
{
    // Platform-relevant workspace events; routine content work (drafts, reviews) is not shown on the system audit.
    private static readonly string[] SystemAuditActions =
    [
        AuditActions.WorkspaceCreated, AuditActions.SocialAccountCreated, AuditActions.SocialConnectionConnected,
        AuditActions.SocialConnectionDisconnected, AuditActions.PublicationActionRequired, AuditActions.PublicationReconciliationRequired,
    ];

    private static readonly DeliveryState[] Due = [DeliveryState.SCHEDULED, DeliveryState.RETRY_PENDING, DeliveryState.WAITING_MEDIA];

    public async Task<PlatformStats> GetAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        var today = utcNow.Date;
        var weekAgo = utcNow.AddDays(-7);
        var hourAgo = utcNow.AddHours(-1);
        var dayAgo = utcNow.AddHours(-24);

        var stopwatch = Stopwatch.StartNew();
        await db.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);
        var databaseLatencyMs = (int)stopwatch.ElapsedMilliseconds;

        // Workspaces
        var organizations = db.Organizations.AsNoTracking();
        var latestWorkspaces = await organizations
            .OrderByDescending(o => o.CreatedAtUtc).Take(5)
            .Select(o => new WorkspaceRowDto(o.Id, o.Name, o.CreatedAtUtc,
                db.OrganizationMembers.Count(m => m.OrganizationId == o.Id && m.Status == MembershipStatus.Active),
                db.Campaigns.Count(c => c.OrganizationId == o.Id),
                o.Status))
            .ToListAsync(cancellationToken);
        var workspaces = new WorkspaceOverviewDto(
            await organizations.CountAsync(cancellationToken),
            await organizations.CountAsync(o => o.Status == OrganizationStatus.Active, cancellationToken),
            await organizations.CountAsync(o => o.Status == OrganizationStatus.Suspended, cancellationToken),
            await organizations.CountAsync(o => o.CreatedAtUtc >= weekAgo, cancellationToken),
            latestWorkspaces);

        // Customer users (system administrators excluded)
        var customers = db.Users.AsNoTracking().Where(u => !u.IsSystemAdmin);
        var users = new UserOverviewDto(
            await customers.CountAsync(cancellationToken),
            await customers.CountAsync(u => u.IsActive, cancellationToken),
            await customers.CountAsync(u => u.CreatedAtUtc >= weekAgo, cancellationToken),
            await customers.CountAsync(u => !u.IsActive, cancellationToken),
            await customers.OrderByDescending(u => u.CreatedAtUtc).Take(5)
                .Select(u => new UserRowDto(u.DisplayName, u.Email, u.CreatedAtUtc)).ToListAsync(cancellationToken));

        // AI
        var requests = db.AiGenerationRequests.AsNoTracking();
        var pending = requests.Where(r => r.Status == AiRequestStatus.PENDING);
        var aiQueue = new AiQueueSummaryDto(
            await pending.CountAsync(cancellationToken),
            await requests.CountAsync(r => r.Status == AiRequestStatus.PROCESSING, cancellationToken),
            await pending.CountAsync(r => r.AttemptCount > 0, cancellationToken),
            await requests.CountAsync(r => r.Status == AiRequestStatus.FAILED && r.CompletedAtUtc >= today, cancellationToken),
            await pending.MinAsync(r => (DateTime?)r.CreatedAtUtc, cancellationToken));
        var completedToday = await requests.CountAsync(r => r.Status == AiRequestStatus.COMPLETED && r.CompletedAtUtc >= today, cancellationToken);
        var callsToday = db.AiCallLogs.AsNoTracking().Where(c => c.CreatedAtUtc >= today);
        var ai = new AiOperationsDto(
            await requests.CountAsync(r => r.CreatedAtUtc >= today, cancellationToken),
            completedToday,
            aiQueue.FailedToday,
            completedToday + aiQueue.FailedToday == 0 ? null : Math.Round(100.0 * completedToday / (completedToday + aiQueue.FailedToday), 1),
            (int?)await callsToday.AverageAsync(c => (double?)c.LatencyMs, cancellationToken),
            await callsToday.AnyAsync(c => c.InputTokens != null || c.OutputTokens != null, cancellationToken)
                ? await callsToday.SumAsync(c => (long)(c.InputTokens ?? 0) + (c.OutputTokens ?? 0), cancellationToken)
                : null,
            await callsToday.AnyAsync(c => c.EstimatedCostUsd != null, cancellationToken)
                ? await callsToday.SumAsync(c => c.EstimatedCostUsd ?? 0, cancellationToken)
                : null);
        var callsLastHour = db.AiCallLogs.AsNoTracking().Where(c => c.CreatedAtUtc >= hourAgo);
        var llm = new LlmActivity(
            await callsLastHour.CountAsync(c => c.Succeeded, cancellationToken),
            await callsLastHour.CountAsync(c => !c.Succeeded, cancellationToken),
            (int?)await callsLastHour.AverageAsync(c => (double?)c.LatencyMs, cancellationToken),
            await db.AiCallLogs.AsNoTracking().Where(c => c.Succeeded).MaxAsync(c => (DateTime?)c.CreatedAtUtc, cancellationToken));

        // Publishing
        var jobs = db.PublicationJobs.AsNoTracking();
        int Count(DeliveryState state, IReadOnlyDictionary<DeliveryState, int> counts) => counts.GetValueOrDefault(state);
        var byState = await jobs.GroupBy(j => j.DeliveryState).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var publishingQueue = new PublishingQueueSummaryDto(
            Count(DeliveryState.SCHEDULED, byState), Count(DeliveryState.PROCESSING, byState), Count(DeliveryState.WAITING_MEDIA, byState),
            Count(DeliveryState.RETRY_PENDING, byState), Count(DeliveryState.ACTION_REQUIRED, byState), Count(DeliveryState.RECONCILIATION_REQUIRED, byState),
            await jobs.Where(j => Due.Contains(j.DeliveryState) && j.NextActionAtUtc <= utcNow).MinAsync(j => j.NextActionAtUtc, cancellationToken));
        var todayByPlatform = await jobs
            .Where(j => j.CompletedAtUtc >= today && (j.DeliveryState == DeliveryState.PUBLISHED || j.DeliveryState == DeliveryState.FAILED || j.DeliveryState == DeliveryState.SIMULATED))
            .GroupBy(j => new { j.Platform, j.DeliveryState }).Select(g => new { g.Key.Platform, g.Key.DeliveryState, Count = g.Count() })
            .ToListAsync(cancellationToken);
        int Today(DeliveryState state, SocialPlatform? platform = null) =>
            todayByPlatform.Where(x => x.DeliveryState == state && (platform == null || x.Platform == platform)).Sum(x => x.Count);
        var publishing = new PublishingOperationsDto(
            await jobs.CountAsync(j => j.DeliveryState == DeliveryState.SCHEDULED && j.PublishAtUtc >= utcNow && j.PublishAtUtc < utcNow.AddHours(1), cancellationToken),
            Today(DeliveryState.PUBLISHED),
            Today(DeliveryState.SIMULATED),
            Today(DeliveryState.FAILED),
            publishingQueue.ActionRequired,
            publishingQueue.ReconciliationRequired,
            Enum.GetValues<SocialPlatform>().Select(p => new PlatformPublishingDto(p, Today(DeliveryState.PUBLISHED, p), Today(DeliveryState.FAILED, p))).ToList());

        var reconciliation = await (
            from j in jobs
            join o in db.Organizations on j.OrganizationId equals o.Id
            where j.DeliveryState == DeliveryState.RECONCILIATION_REQUIRED
            orderby j.UpdatedAtUtc descending
            select new ReconciliationItemDto(j.Id, o.Name, j.Platform, j.LastErrorCode, j.LastErrorMessage, j.UpdatedAtUtc))
            .Take(5).ToListAsync(cancellationToken);

        // Provider traffic: live and test-account attempts only (simulation never reaches a provider).
        var providerRows = await (
            from a in db.PublicationAttempts
            join j in db.PublicationJobs on a.PublicationJobId equals j.Id
            where j.ExecutionMode == ExecutionMode.LIVE || j.ExecutionMode == ExecutionMode.TEST_ACCOUNT
            group a by j.Platform into g
            select new
            {
                Platform = g.Key,
                LastSuccess = g.Max(a => a.ResultState == DeliveryState.PUBLISHED ? a.FinishedAtUtc : null),
                Successes = g.Count(a => a.StartedAtUtc >= dayAgo && a.ResultState == DeliveryState.PUBLISHED),
                Failures = g.Count(a => a.StartedAtUtc >= dayAgo && a.ErrorCategory != null),
            }).ToListAsync(cancellationToken);
        var providers = providerRows.Select(r => new ProviderActivity(r.Platform, r.LastSuccess, r.Successes, r.Failures)).ToList();

        // Customer destinations (no tokens: only status columns are read).
        var accountRows = await db.SocialAccounts.AsNoTracking()
            .Where(a => a.ExecutionMode != ExecutionMode.NOT_CONFIGURED)
            .GroupBy(a => new { a.Platform, Demo = a.ExecutionMode == ExecutionMode.SIMULATED, a.ConnectionStatus })
            .Select(g => new { g.Key.Platform, g.Key.Demo, g.Key.ConnectionStatus, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var connections = Enum.GetValues<SocialPlatform>().Select(p =>
        {
            var real = accountRows.Where(r => r.Platform == p && !r.Demo).ToList();
            return new ConnectionHealthDto(p,
                real.Where(r => r.ConnectionStatus == ConnectionStatus.CONNECTED).Sum(r => r.Count),
                real.Where(r => r.ConnectionStatus == ConnectionStatus.REAUTHORIZATION_REQUIRED).Sum(r => r.Count),
                real.Where(r => r.ConnectionStatus == ConnectionStatus.DISCONNECTED).Sum(r => r.Count),
                accountRows.Where(r => r.Platform == p && r.Demo).Sum(r => r.Count));
        }).ToList();

        var audit = await (
            from l in db.AuditLogs.AsNoTracking()
            join o in db.Organizations on l.OrganizationId equals o.Id
            join u in db.Users on l.ActorUserId equals u.Id into actors
            from u in actors.DefaultIfEmpty()
            where SystemAuditActions.Contains(l.Action)
            orderby l.CreatedAtUtc descending
            select new AuditItemDto(l.Id, u == null ? null : u.Email, l.Action, o.Name, l.CreatedAtUtc))
            .Take(8).ToListAsync(cancellationToken);

        var currentJobs = new Dictionary<string, int>();
        foreach (var row in await jobs.Where(j => j.DeliveryState == DeliveryState.PROCESSING && j.LockedBy != null)
                     .GroupBy(j => j.LockedBy!).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken))
            currentJobs[row.Key] = row.Count;
        foreach (var row in await requests.Where(r => r.Status == AiRequestStatus.PROCESSING && r.LeasedBy != null)
                     .GroupBy(r => r.LeasedBy!).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken))
            currentJobs[row.Key] = currentJobs.GetValueOrDefault(row.Key) + row.Count;

        return new PlatformStats(databaseLatencyMs, workspaces, users, new OverviewQueuesDto(aiQueue, publishingQueue), ai, publishing,
            reconciliation, connections, audit, llm, providers, currentJobs);
    }
}
