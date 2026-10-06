using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using SocialMediaAutomation.Application.Platform;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Infrastructure.Persistence.Repositories;

/// <summary>
/// Read queries for the internal System Admin area. Operational metadata only: never post content, prompts, input
/// snapshots, payload JSON, tokens or password hashes are selected.
/// ponytail: lists are capped (200-500 rows) instead of paged; add server-side paging when volumes grow.
/// </summary>
internal sealed class InternalQueries(CoreDbContext db) : IInternalQueries
{
    private const int ListCap = 500;

    // ---- Workspaces ----

    public async Task<IReadOnlyList<WorkspaceListItemDto>> ListWorkspacesAsync(string? search, OrganizationStatus? status, DateTime? createdFromUtc, CancellationToken cancellationToken)
    {
        var organizations = db.Organizations.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
            organizations = organizations.Where(o => o.Name.Contains(search.Trim()));
        if (status is { } s)
            organizations = organizations.Where(o => o.Status == s);
        if (createdFromUtc is { } from)
            organizations = organizations.Where(o => o.CreatedAtUtc >= from);

        var rows = await organizations
            .OrderByDescending(o => o.CreatedAtUtc)
            .Take(ListCap)
            .Select(o => new
            {
                o.Id, o.Name, o.Status, o.CreatedAtUtc,
                Members = db.OrganizationMembers.Count(m => m.OrganizationId == o.Id && m.Status == MembershipStatus.Active),
                Campaigns = db.Campaigns.Count(c => c.OrganizationId == o.Id),
                SocialAccounts = db.SocialAccounts.Count(a => a.OrganizationId == o.Id && a.ConnectionStatus == ConnectionStatus.CONNECTED),
                LastActivity = db.AuditLogs.Where(l => l.OrganizationId == o.Id).Max(l => (DateTime?)l.CreatedAtUtc),
                Admin = (from m in db.OrganizationMembers
                         join u in db.Users on m.UserId equals u.Id
                         where m.OrganizationId == o.Id && m.Role == OrganizationRole.Admin && m.Status == MembershipStatus.Active
                         orderby m.CreatedAtUtc
                         select new { u.DisplayName, u.Email }).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new WorkspaceListItemDto(r.Id, r.Name, r.Admin?.DisplayName, r.Admin?.Email, r.Members, r.Campaigns, r.SocialAccounts,
            r.Status, r.CreatedAtUtc, r.LastActivity)).ToList();
    }

    public async Task<WorkspaceDetailDto?> GetWorkspaceAsync(Guid id, CancellationToken cancellationToken)
    {
        var o = await db.Organizations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (o is null)
            return null;

        var members = await (from m in db.OrganizationMembers.AsNoTracking()
                             join u in db.Users on m.UserId equals u.Id
                             where m.OrganizationId == id
                             orderby m.Role, u.DisplayName
                             select new WorkspaceMemberDto(u.Id, u.DisplayName, u.Email, m.Role, m.Status))
            .ToListAsync(cancellationToken);
        var accounts = await db.SocialAccounts.AsNoTracking()
            .Where(a => a.OrganizationId == id && a.ExecutionMode != ExecutionMode.NOT_CONFIGURED)
            .OrderBy(a => a.Platform)
            .Select(a => new { a.Platform, a.DisplayName, a.ExecutionMode, a.ConnectionStatus })
            .ToListAsync(cancellationToken);

        var usage = new WorkspaceUsageDto(
            members.Count(m => m.Status == MembershipStatus.Active),
            await db.Campaigns.CountAsync(c => c.OrganizationId == id, cancellationToken),
            await db.Posts.CountAsync(p => p.OrganizationId == id, cancellationToken),
            accounts.Count(a => a.ConnectionStatus == ConnectionStatus.CONNECTED),
            await db.AiGenerationRequests.CountAsync(r => r.OrganizationId == id, cancellationToken),
            await db.PublicationJobs.CountAsync(j => j.OrganizationId == id && j.DeliveryState == DeliveryState.PUBLISHED, cancellationToken));

        return new WorkspaceDetailDto(o.Id, o.Name, o.TimeZoneId, o.CreatedAtUtc, o.Status, usage, members,
            accounts.Select(a => new WorkspaceConnectionDto(a.Platform, a.DisplayName, a.ExecutionMode == ExecutionMode.SIMULATED ? "Demo Mode" : a.ConnectionStatus switch
            {
                ConnectionStatus.CONNECTED => "Connected",
                ConnectionStatus.REAUTHORIZATION_REQUIRED => "Needs Reconnection",
                _ => "Disconnected",
            })).ToList());
    }

    // ---- Users ----

    public async Task<IReadOnlyList<UserListItemDto>> ListUsersAsync(string? search, bool? active, Guid? organizationId, DateTime? createdFromUtc, CancellationToken cancellationToken)
    {
        var users = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            users = users.Where(u => u.DisplayName.Contains(term) || u.Email.Contains(term));
        }
        if (active is { } isActive)
            users = users.Where(u => u.IsActive == isActive);
        if (organizationId is { } orgId)
            users = users.Where(u => db.OrganizationMembers.Any(m => m.UserId == u.Id && m.OrganizationId == orgId));
        if (createdFromUtc is { } from)
            users = users.Where(u => u.CreatedAtUtc >= from);

        var rows = await users.OrderByDescending(u => u.CreatedAtUtc).Take(ListCap)
            .Select(u => new { u.Id, u.DisplayName, u.Email, u.IsActive, u.IsSystemAdmin, u.CreatedAtUtc })
            .ToListAsync(cancellationToken);
        return await WithMembershipsAsync(rows.Select(r => (r.Id, r.DisplayName, r.Email, r.IsActive, r.IsSystemAdmin, r.CreatedAtUtc)).ToList(), cancellationToken);
    }

    public async Task<UserListItemDto?> GetUserAsync(Guid id, CancellationToken cancellationToken)
    {
        var u = await db.Users.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new { x.Id, x.DisplayName, x.Email, x.IsActive, x.IsSystemAdmin, x.CreatedAtUtc })
            .SingleOrDefaultAsync(cancellationToken);
        return u is null ? null : (await WithMembershipsAsync([(u.Id, u.DisplayName, u.Email, u.IsActive, u.IsSystemAdmin, u.CreatedAtUtc)], cancellationToken))[0];
    }

    private async Task<IReadOnlyList<UserListItemDto>> WithMembershipsAsync(
        List<(Guid Id, string Name, string Email, bool IsActive, bool IsSystemAdmin, DateTime CreatedAtUtc)> users, CancellationToken cancellationToken)
    {
        var ids = users.Select(u => u.Id).ToList();
        var memberships = await (from m in db.OrganizationMembers.AsNoTracking()
                                 join o in db.Organizations on m.OrganizationId equals o.Id
                                 where ids.Contains(m.UserId)
                                 select new { m.UserId, Dto = new UserMembershipDto(o.Id, o.Name, m.Role, m.Status) })
            .ToListAsync(cancellationToken);
        var lastLogins = await db.SignInEvents.AsNoTracking()
            .Where(e => e.UserId != null && ids.Contains(e.UserId.Value) && e.EventType == SignInEvents.Succeeded)
            .GroupBy(e => e.UserId!.Value)
            .Select(g => new { UserId = g.Key, Last = g.Max(e => e.CreatedAtUtc) })
            .ToDictionaryAsync(x => x.UserId, x => x.Last, cancellationToken);
        return users.Select(u => new UserListItemDto(u.Id, u.Name, u.Email, memberships.Where(m => m.UserId == u.Id).Select(m => m.Dto).ToList(),
            u.IsActive, u.IsSystemAdmin, u.CreatedAtUtc, lastLogins.TryGetValue(u.Id, out var last) ? last : null)).ToList();
    }

    // ---- Workers & queues ----

    public async Task<IReadOnlyDictionary<string, int>> CurrentJobsByWorkerAsync(CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, int>();
        foreach (var row in await db.PublicationJobs.AsNoTracking().Where(j => j.DeliveryState == DeliveryState.PROCESSING && j.LockedBy != null)
                     .GroupBy(j => j.LockedBy!).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken))
            result[row.Key] = row.Count;
        foreach (var row in await db.AiGenerationRequests.AsNoTracking().Where(r => r.Status == AiRequestStatus.PROCESSING && r.LeasedBy != null)
                     .GroupBy(r => r.LeasedBy!).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken))
            result[row.Key] = result.GetValueOrDefault(row.Key) + row.Count;
        return result;
    }

    public async Task<AiQueueViewDto> AiQueueAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        var dayAgo = utcNow.AddDays(-1);
        var requests = db.AiGenerationRequests.AsNoTracking();
        var summary = new AiQueueSummaryItemsDto(
            await requests.CountAsync(r => r.Status == AiRequestStatus.PENDING && r.AttemptCount == 0, cancellationToken),
            await requests.CountAsync(r => r.Status == AiRequestStatus.PROCESSING, cancellationToken),
            await requests.CountAsync(r => r.Status == AiRequestStatus.PENDING && r.AttemptCount > 0, cancellationToken),
            await requests.CountAsync(r => r.Status == AiRequestStatus.FAILED && r.CreatedAtUtc >= dayAgo, cancellationToken),
            await requests.Where(r => r.Status == AiRequestStatus.PENDING).MinAsync(r => (DateTime?)r.CreatedAtUtc, cancellationToken));

        var rows = await (from r in requests
                          join o in db.Organizations on r.OrganizationId equals o.Id
                          where r.Status == AiRequestStatus.PENDING || r.Status == AiRequestStatus.PROCESSING
                              || (r.Status == AiRequestStatus.FAILED && r.CreatedAtUtc >= dayAgo)
                          orderby r.CreatedAtUtc descending
                          select new { r.Id, r.OrganizationId, o.Name, r.Kind, r.Status, r.AttemptCount, r.MaxAttempts, r.CreatedAtUtc, r.StartedAtUtc, r.CompletedAtUtc, r.FailureCode, r.FailureMessage, r.CorrelationId })
            .Take(200).ToListAsync(cancellationToken);

        return new AiQueueViewDto(summary, rows.Select(r => new AiQueueItemDto(r.Id, r.OrganizationId, r.Name, Operation(r.Kind),
            r.Status == AiRequestStatus.PENDING && r.AttemptCount > 0 ? "RETRY_PENDING" : r.Status.ToString(), r.AttemptCount, r.MaxAttempts, r.CreatedAtUtc,
            r.StartedAtUtc, r.StartedAtUtc is { } s && r.CompletedAtUtc is { } c ? (int)(c - s).TotalMilliseconds : null,
            r.FailureCode is null ? null : $"{r.FailureCode}: {r.FailureMessage}", r.CorrelationId)).ToList());
    }

    public async Task<PublishingQueueViewDto> PublishingQueueAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        var dayAgo = utcNow.AddDays(-1);
        var jobs = db.PublicationJobs.AsNoTracking();
        var counts = await jobs.GroupBy(j => j.DeliveryState).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        var summary = new PublishingQueueSummaryItemsDto(
            counts.GetValueOrDefault(DeliveryState.SCHEDULED), counts.GetValueOrDefault(DeliveryState.PROCESSING), counts.GetValueOrDefault(DeliveryState.WAITING_MEDIA),
            counts.GetValueOrDefault(DeliveryState.RETRY_PENDING), counts.GetValueOrDefault(DeliveryState.ACTION_REQUIRED),
            counts.GetValueOrDefault(DeliveryState.RECONCILIATION_REQUIRED),
            await jobs.CountAsync(j => j.DeliveryState == DeliveryState.FAILED && j.UpdatedAtUtc >= dayAgo, cancellationToken));

        DeliveryState[] open = [DeliveryState.SCHEDULED, DeliveryState.PROCESSING, DeliveryState.WAITING_MEDIA, DeliveryState.RETRY_PENDING,
            DeliveryState.ACTION_REQUIRED, DeliveryState.RECONCILIATION_REQUIRED];
        var items = await Publications(jobs.Where(j => open.Contains(j.DeliveryState) || (j.DeliveryState == DeliveryState.FAILED && j.UpdatedAtUtc >= dayAgo))
            .OrderBy(j => j.PublishAtUtc).Take(200)).ToListAsync(cancellationToken);
        return new PublishingQueueViewDto(summary, items.OrderBy(p => p.PublishAtUtc).ToList());
    }

    // ---- Publications ----

    public async Task<IReadOnlyList<PublicationListItemDto>> ListPublicationsAsync(PublicationQueryDto query, CancellationToken cancellationToken)
    {
        var jobs = db.PublicationJobs.AsNoTracking();
        if (query.OrganizationId is { } orgId) jobs = jobs.Where(j => j.OrganizationId == orgId);
        if (query.Platform is { } platform) jobs = jobs.Where(j => j.Platform == platform);
        if (query.FromUtc is { } from) jobs = jobs.Where(j => j.PublishAtUtc >= from);
        if (query.ToUtc is { } to) jobs = jobs.Where(j => j.PublishAtUtc < to);
        if (query.States is { Count: > 0 } states) jobs = jobs.Where(j => states.Contains(j.DeliveryState));
        return (await Publications(jobs.OrderByDescending(j => j.PublishAtUtc).Take(ListCap)).ToListAsync(cancellationToken))
            .OrderByDescending(p => p.PublishAtUtc).ToList();
    }

    public async Task<(PublicationListItemDto Publication, PublicationJob Job, IReadOnlyList<PublicationAttemptAdminDto> Attempts)?> GetPublicationAsync(Guid id, CancellationToken cancellationToken)
    {
        var job = await db.PublicationJobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == id, cancellationToken);
        if (job is null)
            return null;
        var item = await Publications(db.PublicationJobs.AsNoTracking().Where(j => j.Id == id)).SingleAsync(cancellationToken);
        var attempts = await db.PublicationAttempts.AsNoTracking().Where(a => a.PublicationJobId == id).OrderBy(a => a.StartedAtUtc)
            .Select(a => new PublicationAttemptAdminDto(a.Id, a.AttemptNumber, a.Step, a.WorkerId, a.StartedAtUtc, a.FinishedAtUtc, a.ResultState, a.ErrorCategory,
                a.ErrorCode, a.ErrorMessage, a.ProviderRequestId, a.ExternalId, a.Detail))
            .ToListAsync(cancellationToken);
        return (item, job, attempts);
    }

    /// <summary>
    /// Joins the display names onto already filtered, sorted and limited jobs. Sort before calling: EF cannot order by a
    /// property of the constructed DTO, and callers re-sort the (capped) result in memory, since SQL does not guarantee a subquery.s order.
    /// </summary>
    private IQueryable<PublicationListItemDto> Publications(IQueryable<PublicationJob> jobs) =>
        from j in jobs
        join o in db.Organizations on j.OrganizationId equals o.Id
        join p in db.Posts on j.PostId equals p.Id
        join c in db.Campaigns on p.CampaignId equals c.Id into folders
        from c in folders.DefaultIfEmpty()
        join v in db.PostVersions on j.PostVersionId equals v.Id
        join a in db.SocialAccounts on j.SocialAccountId equals a.Id
        select new PublicationListItemDto(j.Id, j.OrganizationId, o.Name, p.CampaignId, c == null ? null : c.Name, p.Id, v.VersionNumber, j.Platform, a.DisplayName, j.ExecutionMode,
            j.PublishAtUtc, j.DeliveryState, j.AttemptCount, j.MaxAttempts, j.LockedBy, j.LastErrorCode, j.LastErrorMessage, j.CorrelationId, j.CreatedAtUtc);

    // ---- Logs ----

    public async Task<IReadOnlyList<AuditLogItemDto>> AuditLogAsync(LogQueryDto query, Guid? actorUserId, Guid? organizationId, CancellationToken cancellationToken)
    {
        var take = Math.Clamp(query.Take, 1, ListCap);
        var platform = db.PlatformAuditLogs.AsNoTracking();
        var workspace = db.AuditLogs.AsNoTracking();
        if (query.FromUtc is { } from) { platform = platform.Where(a => a.CreatedAtUtc >= from); workspace = workspace.Where(a => a.CreatedAtUtc >= from); }
        if (query.ToUtc is { } to) { platform = platform.Where(a => a.CreatedAtUtc < to); workspace = workspace.Where(a => a.CreatedAtUtc < to); }
        if (actorUserId is { } actor) { platform = platform.Where(a => a.ActorUserId == actor); workspace = workspace.Where(a => a.ActorUserId == actor); }
        if (organizationId is { } org) { platform = platform.Where(a => a.OrganizationId == org); workspace = workspace.Where(a => a.OrganizationId == org); }
        if (!string.IsNullOrWhiteSpace(query.CorrelationId)) { platform = platform.Where(a => a.CorrelationId == query.CorrelationId); workspace = workspace.Where(_ => false); }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            platform = platform.Where(a => a.Action.Contains(term) || a.TargetType.Contains(term) || (a.TargetLabel != null && a.TargetLabel.Contains(term)));
            workspace = workspace.Where(a => a.Action.Contains(term) || a.ResourceType.Contains(term));
        }

        var platformRows = await (from a in platform
                                  join u in db.Users on a.ActorUserId equals u.Id into actors
                                  from u in actors.DefaultIfEmpty()
                                  join o in db.Organizations on a.OrganizationId equals o.Id into orgs
                                  from o in orgs.DefaultIfEmpty()
                                  orderby a.CreatedAtUtc descending
                                  select new AuditLogItemDto(a.Id, a.CreatedAtUtc, u == null ? null : u.Email, a.Action, a.TargetType, a.TargetLabel ?? a.TargetId,
                                      o == null ? null : o.Name, a.Result, a.CorrelationId, "PLATFORM"))
            .Take(take).ToListAsync(cancellationToken);
        var workspaceRows = await (from a in workspace
                                   join u in db.Users on a.ActorUserId equals u.Id into actors
                                   from u in actors.DefaultIfEmpty()
                                   join o in db.Organizations on a.OrganizationId equals o.Id
                                   orderby a.CreatedAtUtc descending
                                   select new AuditLogItemDto(a.Id, a.CreatedAtUtc, u == null ? null : u.Email, a.Action, a.ResourceType, a.ResourceId.ToString(),
                                       o.Name, "SUCCESS", null, "WORKSPACE"))
            .Take(take).ToListAsync(cancellationToken);
        return platformRows.Concat(workspaceRows).OrderByDescending(a => a.AtUtc).Take(take).ToList();
    }

    public async Task<IReadOnlyList<SignInLogItemDto>> SignInLogAsync(LogQueryDto query, bool? succeeded, Guid? userId, string? ipAddress, CancellationToken cancellationToken)
    {
        var events = db.SignInEvents.AsNoTracking();
        if (query.FromUtc is { } from) events = events.Where(e => e.CreatedAtUtc >= from);
        if (query.ToUtc is { } to) events = events.Where(e => e.CreatedAtUtc < to);
        if (succeeded is { } ok) events = events.Where(e => e.Succeeded == ok);
        if (userId is { } id) events = events.Where(e => e.UserId == id);
        if (!string.IsNullOrWhiteSpace(ipAddress)) events = events.Where(e => e.IpAddress == ipAddress.Trim());
        if (!string.IsNullOrWhiteSpace(query.CorrelationId)) events = events.Where(e => e.CorrelationId == query.CorrelationId);
        if (!string.IsNullOrWhiteSpace(query.Search)) events = events.Where(e => e.Email.Contains(query.Search.Trim()));
        return await (from e in events
                      join u in db.Users on e.UserId equals u.Id into users
                      from u in users.DefaultIfEmpty()
                      orderby e.CreatedAtUtc descending
                      select new SignInLogItemDto(e.Id, e.CreatedAtUtc, e.UserId, u == null ? null : u.DisplayName, e.Email, e.Area, e.EventType, e.Succeeded,
                          e.IpAddress, e.UserAgent, e.CorrelationId))
            .Take(Math.Clamp(query.Take, 1, ListCap)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AiLogItemDto>> AiLogAsync(LogQueryDto query, CancellationToken cancellationToken)
    {
        var calls = db.AiCallLogs.AsNoTracking();
        if (query.FromUtc is { } from) calls = calls.Where(c => c.CreatedAtUtc >= from);
        if (query.ToUtc is { } to) calls = calls.Where(c => c.CreatedAtUtc < to);
        var rows = await (from c in calls
                          join r in db.AiGenerationRequests on c.RequestId equals r.Id
                          join o in db.Organizations on c.OrganizationId equals o.Id
                          where string.IsNullOrEmpty(query.CorrelationId) || r.CorrelationId == query.CorrelationId
                          where string.IsNullOrEmpty(query.Search) || o.Name.Contains(query.Search) || c.Model.Contains(query.Search)
                          orderby c.CreatedAtUtc descending
                          select new { c.Id, c.RequestId, o.Name, c.Purpose, c.Model, c.Succeeded, c.CreatedAtUtc, c.LatencyMs, c.InputTokens, c.OutputTokens,
                              r.AttemptCount, c.ErrorCode, c.HttpStatus, r.CorrelationId })
            .Take(Math.Clamp(query.Take, 1, ListCap)).ToListAsync(cancellationToken);
        // Diagnostics (raw model output) are deliberately not selected: they can contain customer content.
        return rows.Select(r => new AiLogItemDto(r.Id, r.RequestId, r.Name, Operation(r.Purpose), r.Model, r.Succeeded, r.CreatedAtUtc.AddMilliseconds(-r.LatencyMs),
            r.LatencyMs, r.InputTokens, r.OutputTokens, r.AttemptCount, r.ErrorCode is null ? null : $"{r.ErrorCode}{(r.HttpStatus is { } s ? $" (HTTP {s})" : "")}",
            r.CorrelationId)).ToList();
    }

    public async Task<IReadOnlyList<PublishingLogItemDto>> PublishingLogAsync(LogQueryDto query, CancellationToken cancellationToken)
    {
        var take = Math.Clamp(query.Take, 1, ListCap);
        var attempts = db.PublicationAttempts.AsNoTracking();
        var jobs = db.PublicationJobs.AsNoTracking();
        if (query.FromUtc is { } from) { attempts = attempts.Where(a => a.StartedAtUtc >= from); jobs = jobs.Where(j => j.CreatedAtUtc >= from); }
        if (query.ToUtc is { } to) { attempts = attempts.Where(a => a.StartedAtUtc < to); jobs = jobs.Where(j => j.CreatedAtUtc < to); }
        if (!string.IsNullOrWhiteSpace(query.CorrelationId)) jobs = jobs.Where(j => j.CorrelationId == query.CorrelationId);

        var attemptRows = await (from a in attempts
                                 join j in jobs on a.PublicationJobId equals j.Id
                                 join o in db.Organizations on a.OrganizationId equals o.Id
                                 orderby a.StartedAtUtc descending
                                 select new { a.Id, a.StartedAtUtc, a.FinishedAtUtc, JobId = j.Id, o.Name, j.Platform, a.Step, a.ResultState, a.ProviderRequestId,
                                     a.AttemptNumber, a.ErrorCategory, a.ErrorCode, j.CorrelationId })
            .Take(take).ToListAsync(cancellationToken);
        var jobRows = await (from j in jobs
                             join o in db.Organizations on j.OrganizationId equals o.Id
                             orderby j.CreatedAtUtc descending
                             select new { j.Id, j.CreatedAtUtc, j.CanceledAtUtc, o.Name, j.Platform, j.LocalPublishAt, j.CorrelationId })
            .Take(take).ToListAsync(cancellationToken);

        var items = attemptRows.Select(a => new PublishingLogItemDto(a.Id, a.StartedAtUtc, a.JobId, a.Name, a.Platform,
            a.Step == PublicationAttemptStep.MEDIA_STATUS ? "Media status check" : "Provider request",
            a.ResultState?.ToString() ?? (a.FinishedAtUtc is null ? "IN_PROGRESS" : "UNKNOWN"), a.ProviderRequestId,
            a.FinishedAtUtc is { } f ? (int)(f - a.StartedAtUtc).TotalMilliseconds : null, a.AttemptNumber, a.ErrorCategory?.ToString(), a.ErrorCode, a.CorrelationId))
            .Concat(jobRows.Select(j => new PublishingLogItemDto(j.Id, j.CreatedAtUtc, j.Id, j.Name, j.Platform,
                j.LocalPublishAt is null ? "Publish Now requested" : "Job scheduled", "SCHEDULED", null, null, null, null, null, j.CorrelationId)))
            .Concat(jobRows.Where(j => j.CanceledAtUtc is not null).Select(j => new PublishingLogItemDto(j.Id, j.CanceledAtUtc!.Value, j.Id, j.Name, j.Platform,
                "Canceled", "CANCELED", null, null, null, null, null, j.CorrelationId)));
        if (!string.IsNullOrWhiteSpace(query.Search))
            items = items.Where(i => i.Workspace.Contains(query.Search, StringComparison.OrdinalIgnoreCase) || (i.Error?.Contains(query.Search, StringComparison.OrdinalIgnoreCase) ?? false));
        return items.OrderByDescending(i => i.AtUtc).Take(take).ToList();
    }

    /// <summary>Recorded system events plus provider-call failures already captured by AI calls and publication attempts.</summary>
    public async Task<IReadOnlyList<SystemLogItemDto>> SystemLogAsync(LogQueryDto query, string? level, string? service, CancellationToken cancellationToken)
    {
        var take = Math.Clamp(query.Take, 1, ListCap);
        var events = db.SystemEvents.AsNoTracking();
        var calls = db.AiCallLogs.AsNoTracking().Where(c => !c.Succeeded);
        var attempts = db.PublicationAttempts.AsNoTracking().Where(a => a.ErrorCode != null);
        if (query.FromUtc is { } from) { events = events.Where(e => e.CreatedAtUtc >= from); calls = calls.Where(c => c.CreatedAtUtc >= from); attempts = attempts.Where(a => a.StartedAtUtc >= from); }
        if (query.ToUtc is { } to) { events = events.Where(e => e.CreatedAtUtc < to); calls = calls.Where(c => c.CreatedAtUtc < to); attempts = attempts.Where(a => a.StartedAtUtc < to); }

        var recorded = await events.OrderByDescending(e => e.CreatedAtUtc).Take(take)
            .Select(e => new SystemLogItemDto(e.Id, e.CreatedAtUtc, e.Level, e.Service, e.EventName, e.Message, e.CorrelationId))
            .ToListAsync(cancellationToken);
        var llm = await (from c in calls
                         join r in db.AiGenerationRequests on c.RequestId equals r.Id
                         orderby c.CreatedAtUtc descending
                         select new { c.Id, c.CreatedAtUtc, c.Provider, c.ErrorCode, c.HttpStatus, c.Model, r.CorrelationId })
            .Take(take).ToListAsync(cancellationToken);
        var provider = await (from a in attempts
                              join j in db.PublicationJobs on a.PublicationJobId equals j.Id
                              orderby a.StartedAtUtc descending
                              select new { a.Id, At = a.FinishedAtUtc ?? a.StartedAtUtc, j.Platform, a.ErrorCategory, a.ErrorCode, a.ErrorMessage, j.CorrelationId })
            .Take(take).ToListAsync(cancellationToken);

        var items = recorded
            .Concat(llm.Select(c => new SystemLogItemDto(c.Id, c.CreatedAtUtc, SystemEventLevels.Warning, c.Provider == AiProvider.OpenAI ? "OpenAI" : c.Provider.ToString(),
                "LLM_CALL_FAILED", $"{c.ErrorCode}{(c.HttpStatus is { } s ? $" (HTTP {s})" : "")} calling model {c.Model}", c.CorrelationId)))
            .Concat(provider.Select(a => new SystemLogItemDto(a.Id, a.At,
                a.ErrorCategory is PublishErrorCategory.UNCERTAIN or PublishErrorCategory.PERMANENT ? SystemEventLevels.Error : SystemEventLevels.Warning,
                a.Platform == SocialPlatform.LinkedIn ? "LinkedIn" : "Meta", "PROVIDER_REQUEST_FAILED",
                Domain.Common.SecretRedactor.Redact($"{a.ErrorCategory} {a.ErrorCode}: {a.ErrorMessage}"), a.CorrelationId)));

        if (!string.IsNullOrWhiteSpace(level)) items = items.Where(i => string.Equals(i.Level, level, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(service)) items = items.Where(i => string.Equals(i.Service, service, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(query.CorrelationId)) items = items.Where(i => i.CorrelationId == query.CorrelationId);
        if (!string.IsNullOrWhiteSpace(query.Search))
            items = items.Where(i => i.Message.Contains(query.Search, StringComparison.OrdinalIgnoreCase) || i.Event.Contains(query.Search, StringComparison.OrdinalIgnoreCase));
        return items.OrderByDescending(i => i.AtUtc).Take(take).ToList();
    }

    // ---- Activity for configuration status and health ----

    public async Task<AiActivityDto> AiActivityAsync(DateTime utcNow, CancellationToken cancellationToken, bool images = false)
    {
        var today = utcNow.Date;
        var hourAgo = utcNow.AddHours(-1);
        var calls = db.AiCallLogs.AsNoTracking().Where(c => c.Purpose != AiCallPurpose.ModelListing && (c.Purpose == AiCallPurpose.ImageGeneration) == images);
        var requests = db.AiGenerationRequests.AsNoTracking().Where(r => (r.Kind == AiRequestKind.ImageGeneration) == images);
        var lastSuccess = await calls.Where(c => c.Succeeded).MaxAsync(c => (DateTime?)c.CreatedAtUtc, cancellationToken);
        var lastFailure = await calls.Where(c => !c.Succeeded).OrderByDescending(c => c.CreatedAtUtc)
            .Select(c => new { c.CreatedAtUtc, c.ErrorCode, c.HttpStatus }).FirstOrDefaultAsync(cancellationToken);
        var lastHour = await calls.Where(c => c.CreatedAtUtc >= hourAgo).Select(c => new { c.Succeeded, c.LatencyMs }).ToListAsync(cancellationToken);
        return new AiActivityDto(lastSuccess, lastFailure?.CreatedAtUtc,
            lastFailure is null ? null : $"{lastFailure.ErrorCode}{(lastFailure.HttpStatus is { } s ? $" (HTTP {s})" : "")}",
            await requests.CountAsync(r => r.CreatedAtUtc >= today, cancellationToken),
            await requests.CountAsync(r => r.CreatedAtUtc >= today && r.Status == AiRequestStatus.FAILED, cancellationToken),
            lastHour.Count, lastHour.Count(c => !c.Succeeded), lastHour.Count > 0 ? (int)lastHour.Average(c => c.LatencyMs) : null);
    }

    public async Task<ProviderActivityDto> ProviderActivityAsync(IReadOnlyCollection<SocialPlatform> platforms, DateTime utcNow, CancellationToken cancellationToken)
    {
        var dayAgo = utcNow.AddDays(-1);
        // Live provider traffic only: simulated jobs never call a provider.
        var attempts = from a in db.PublicationAttempts.AsNoTracking()
                       join j in db.PublicationJobs on a.PublicationJobId equals j.Id
                       where platforms.Contains(j.Platform) && j.ExecutionMode != ExecutionMode.SIMULATED && a.FinishedAtUtc != null
                       select new { a.FinishedAtUtc, a.ErrorCode, a.ErrorMessage };
        var lastSuccess = await attempts.Where(a => a.ErrorCode == null).MaxAsync(a => a.FinishedAtUtc, cancellationToken);
        var lastFailure = await attempts.Where(a => a.ErrorCode != null).OrderByDescending(a => a.FinishedAtUtc).FirstOrDefaultAsync(cancellationToken);
        return new ProviderActivityDto(lastSuccess, lastFailure?.FinishedAtUtc,
            lastFailure is null ? null : Domain.Common.SecretRedactor.Redact($"{lastFailure.ErrorCode}: {lastFailure.ErrorMessage}"),
            await attempts.CountAsync(a => a.ErrorCode != null && a.FinishedAtUtc >= dayAgo, cancellationToken),
            await attempts.CountAsync(a => a.ErrorCode == null && a.FinishedAtUtc >= dayAgo, cancellationToken));
    }

    public async Task<int> DatabaseLatencyMsAsync(CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        await db.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);
        return (int)watch.ElapsedMilliseconds;
    }

    private static string Operation(AiRequestKind kind) => kind == AiRequestKind.Generation ? "Generate" : "Rewrite";

    private static string Operation(AiCallPurpose purpose) => purpose switch
    {
        AiCallPurpose.Generation => "Generate",
        AiCallPurpose.Rewrite => "Rewrite",
        AiCallPurpose.Evaluation => "Evaluate",
        _ => "List models",
    };
}
