using SocialMediaAutomation.Application.AiGeneration.Services;
using SocialMediaAutomation.Application.Common.Authorization;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Application.Operations;

public sealed record WorkerStatusDto(string WorkerId, WorkerKind Kind, DateTime StartedAtUtc, DateTime LastSeenAtUtc, bool IsStale);

/// <summary>Publication queue health for one workspace. "Due" means waiting for a worker right now.</summary>
public sealed record PublicationQueueDto(
    int Due, DateTime? OldestDueAtUtc, int Processing, int ExpiredLeases, int WaitingMedia, int RetryPending, int Throttled,
    int ActionRequired, int ReconciliationRequired);

public sealed record AiQueueDto(int Due, DateTime? OldestDueAtUtc, int Processing, int ExpiredLeases, int FailedLast24Hours);

public sealed record OperationsDto(DateTime GeneratedAtUtc, IReadOnlyList<WorkerStatusDto> Workers, PublicationQueueDto Publications, AiQueueDto Ai);

/// <summary>Which LLM the system uses. Never carries the key itself.</summary>
public sealed record AiConfigurationStatusDto(bool SystemConfigured, string? Provider, string? Model, bool ApiKeyPresent);

/// <summary>Internal system overview across all workspaces (no tenant content, only counts and worker liveness).</summary>
public sealed record SystemOverviewDto(OperationsDto Operations, AiConfigurationStatusDto Ai);

/// <summary>Durable worker liveness, written by the worker loops.</summary>
public interface IWorkerHeartbeats
{
    Task BeatAsync(string workerId, WorkerKind kind, DateTime startedAtUtc, CancellationToken cancellationToken, string? version = null);

    Task<IReadOnlyList<WorkerHeartbeat>> ListAsync(CancellationToken cancellationToken);
}

public interface IOperationsRepository
{
    /// <param name="organizationId">Null for all workspaces (internal system view).</param>
    Task<PublicationQueueDto> PublicationQueueAsync(Guid? organizationId, DateTime utcNow, CancellationToken cancellationToken);

    /// <param name="organizationId">Null for all workspaces (internal system view).</param>
    Task<AiQueueDto> AiQueueAsync(Guid? organizationId, DateTime utcNow, CancellationToken cancellationToken);
}

/// <summary>
/// Internal operations view (TASKS.md Phase 28), restricted to system administrators: infrastructure is not a customer
/// concern, so workspace Admins do not see it: are workers alive, is work waiting too long, are leases expiring, and what
/// needs a person. Queue numbers are scoped to the caller's workspace; worker liveness is system-wide and carries no
/// tenant data.
/// </summary>
public sealed class OperationsService(
    OrganizationAuthorizer authorizer,
    SystemAdminAuthorizer systemAdmins,
    IWorkerHeartbeats heartbeats,
    IOperationsRepository operations,
    SystemAiOptions systemAi,
    TimeProvider timeProvider)
{
    /// <summary>One workspace's queues (system administrators who are also members of that workspace).</summary>
    public async Task<OperationsDto> GetAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        await systemAdmins.RequireAsync(cancellationToken);
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ViewAuditLog, cancellationToken);
        return await BuildAsync(access.OrganizationId, cancellationToken);
    }

    /// <summary>All workspaces, plus which LLM is configured. System administrators only.</summary>
    public async Task<SystemOverviewDto> GetSystemOverviewAsync(CancellationToken cancellationToken)
    {
        await systemAdmins.RequireAsync(cancellationToken);
        return new SystemOverviewDto(
            await BuildAsync(null, cancellationToken),
            new AiConfigurationStatusDto(systemAi.IsConfigured, systemAi.Provider, systemAi.Model, !string.IsNullOrWhiteSpace(systemAi.ApiKey)));
    }

    private async Task<OperationsDto> BuildAsync(Guid? organizationId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var workers = (await heartbeats.ListAsync(cancellationToken))
            .OrderBy(w => w.Kind).ThenByDescending(w => w.LastSeenAtUtc)
            .Select(w => new WorkerStatusDto(w.WorkerId, w.Kind, w.StartedAtUtc, w.LastSeenAtUtc, w.IsStale(now)))
            .ToList();
        return new OperationsDto(now, workers,
            await operations.PublicationQueueAsync(organizationId, now, cancellationToken),
            await operations.AiQueueAsync(organizationId, now, cancellationToken));
    }
}
