using SocialMediaAutomation.Application.Common.Authorization;
using SocialMediaAutomation.Application.Operations;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Application.Platform;

/// <summary>Read-only operations views for system administrators: workers, queues, publications and logs.</summary>
public sealed class InternalOperationsService(
    SystemAdminAuthorizer systemAdmins, IInternalQueries queries, IWorkerHeartbeats heartbeats, TimeProvider timeProvider)
{
    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    public async Task<IReadOnlyList<WorkerDto>> WorkersAsync(CancellationToken cancellationToken)
    {
        await systemAdmins.RequireAsync(cancellationToken);
        var now = Now;
        var jobs = await queries.CurrentJobsByWorkerAsync(cancellationToken);
        return (await heartbeats.ListAsync(cancellationToken))
            .OrderBy(w => w.Kind).ThenByDescending(w => w.LastSeenAtUtc)
            .Select(w => new WorkerDto(w.WorkerId, w.Kind, Instance(w.WorkerId), SystemHealthService.WorkerStatus(w.LastSeenAtUtc, now), w.LastSeenAtUtc,
                jobs.GetValueOrDefault(w.WorkerId), w.StartedAtUtc, (long)(w.LastSeenAtUtc - w.StartedAtUtc).TotalSeconds, w.Version))
            .ToList();
    }

    public async Task<AiQueueViewDto> AiQueueAsync(CancellationToken cancellationToken)
    {
        await systemAdmins.RequireAsync(cancellationToken);
        return await queries.AiQueueAsync(Now, cancellationToken);
    }

    public async Task<PublishingQueueViewDto> PublishingQueueAsync(CancellationToken cancellationToken)
    {
        await systemAdmins.RequireAsync(cancellationToken);
        return await queries.PublishingQueueAsync(Now, cancellationToken);
    }

    public async Task<IReadOnlyList<PublicationListItemDto>> PublicationsAsync(PublicationQueryDto query, CancellationToken cancellationToken)
    {
        await systemAdmins.RequireAsync(cancellationToken);
        return await queries.ListPublicationsAsync(query, cancellationToken);
    }

    /// <summary>The publication with a readable timeline. Payload content, tokens and credentials are never included.</summary>
    public async Task<PublicationDetailDto> PublicationAsync(Guid id, CancellationToken cancellationToken)
    {
        await systemAdmins.RequireAsync(cancellationToken);
        var (publication, job, attempts) = await queries.GetPublicationAsync(id, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.NotFound, "Publication not found.");
        return new PublicationDetailDto(publication, job.PayloadHash, job.ExternalPostId, job.ExternalPostUrl, job.DispatchStartedAtUtc, job.CompletedAtUtc,
            job.CanceledAtUtc, attempts, Timeline(job, publication, attempts));
    }

    public async Task<IReadOnlyList<AuditLogItemDto>> AuditLogAsync(LogQueryDto query, CancellationToken cancellationToken)
    {
        await systemAdmins.RequireAsync(cancellationToken);
        return await queries.AuditLogAsync(query, null, null, cancellationToken);
    }

    public async Task<IReadOnlyList<SignInLogItemDto>> SignInLogAsync(LogQueryDto query, bool? succeeded, Guid? userId, string? ip, CancellationToken cancellationToken)
    {
        await systemAdmins.RequireAsync(cancellationToken);
        return await queries.SignInLogAsync(query, succeeded, userId, ip, cancellationToken);
    }

    public async Task<IReadOnlyList<AiLogItemDto>> AiLogAsync(LogQueryDto query, CancellationToken cancellationToken)
    {
        await systemAdmins.RequireAsync(cancellationToken);
        return await queries.AiLogAsync(query, cancellationToken);
    }

    public async Task<IReadOnlyList<PublishingLogItemDto>> PublishingLogAsync(LogQueryDto query, CancellationToken cancellationToken)
    {
        await systemAdmins.RequireAsync(cancellationToken);
        return await queries.PublishingLogAsync(query, cancellationToken);
    }

    public async Task<IReadOnlyList<SystemLogItemDto>> SystemLogAsync(LogQueryDto query, string? level, string? service, CancellationToken cancellationToken)
    {
        await systemAdmins.RequireAsync(cancellationToken);
        return await queries.SystemLogAsync(query, level, service, cancellationToken);
    }

    /// <summary>"host:pid:kind" worker ids → "host (pid)".</summary>
    private static string Instance(string workerId)
    {
        var parts = workerId.Split(':');
        return parts.Length >= 2 ? $"{parts[0]} (pid {parts[1]})" : workerId;
    }

    private static List<TimelineEventDto> Timeline(PublicationJob job, PublicationListItemDto publication, IReadOnlyList<PublicationAttemptAdminDto> attempts)
    {
        var events = new List<TimelineEventDto>
        {
            new(job.CreatedAtUtc, job.LocalPublishAt is null ? "Publish Now requested" : "Scheduled", $"Due {job.PublishAtUtc:u}", "neutral"),
        };
        foreach (var a in attempts)
        {
            events.Add(new(a.StartedAtUtc, $"Worker claimed (attempt {a.AttemptNumber})", $"{a.WorkerId} · {a.Step}", "neutral"));
            if (a.Step == PublicationAttemptStep.DISPATCH && job.DispatchStartedAtUtc is { } sent && sent >= a.StartedAtUtc
                && (a.FinishedAtUtc is null || sent <= a.FinishedAtUtc))
                events.Add(new(sent, "Provider request sent", publication.Platform.ToString(), "neutral"));
            if (a.FinishedAtUtc is { } finished)
            {
                var detail = string.Join(" · ", new[]
                {
                    a.ResultState?.ToString(), a.ErrorCategory?.ToString(), a.ErrorCode, a.ProviderRequestId is null ? null : $"request {a.ProviderRequestId}",
                    a.ExternalId is null ? null : $"external id {a.ExternalId}",
                }.Where(s => s is not null));
                events.Add(new(finished, "Provider response", detail, a.ErrorCode is null ? "success" : "danger"));
            }
        }

        var final = job.DeliveryState switch
        {
            DeliveryState.PUBLISHED => ("Published", "success"),
            DeliveryState.SIMULATED => ("Simulated (not sent)", "neutral"),
            DeliveryState.FAILED => ("Failed", "danger"),
            DeliveryState.RECONCILIATION_REQUIRED => ("Reconciliation required", "danger"),
            DeliveryState.ACTION_REQUIRED => ("Action required", "warning"),
            DeliveryState.CANCELED => ("Canceled", "neutral"),
            _ => ((string, string)?)null,
        };
        if (final is { } f)
            events.Add(new(job.CompletedAtUtc ?? job.CanceledAtUtc ?? job.UpdatedAtUtc, f.Item1, job.LastErrorMessage, f.Item2));
        return events.OrderBy(e => e.AtUtc).ToList();
    }
}
