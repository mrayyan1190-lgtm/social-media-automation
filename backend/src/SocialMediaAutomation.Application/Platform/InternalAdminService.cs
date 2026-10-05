using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.Audit.Services;
using SocialMediaAutomation.Application.Common.Authorization;
using SocialMediaAutomation.Application.Publications.Services;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Application.Platform;

/// <summary>
/// Tenant administration and manual reconciliation for system administrators. Operators can suspend a workspace or
/// disable an account, never edit customer content. Every change is audited with actor, target, result and correlation id.
/// </summary>
public sealed class InternalAdminService(
    SystemAdminAuthorizer systemAdmins,
    IInternalQueries queries,
    IOrganizationRepository organizations,
    IUserRepository users,
    IPublicationRepository publications,
    ActivityRecorder activity,
    PlatformAudit audit,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    // ---- Workspaces ----

    public async Task<IReadOnlyList<WorkspaceListItemDto>> ListWorkspacesAsync(string? search, OrganizationStatus? status, DateTime? createdFromUtc, CancellationToken cancellationToken)
    {
        await systemAdmins.RequireAsync(cancellationToken);
        return await queries.ListWorkspacesAsync(search, status, createdFromUtc, cancellationToken);
    }

    public async Task<WorkspaceDetailDto> GetWorkspaceAsync(Guid id, CancellationToken cancellationToken)
    {
        await systemAdmins.RequireAsync(cancellationToken);
        return await queries.GetWorkspaceAsync(id, cancellationToken) ?? throw NotFound("Workspace");
    }

    /// <summary>Workspace business events and operator actions on this workspace, newest first.</summary>
    public async Task<IReadOnlyList<AuditLogItemDto>> WorkspaceAuditAsync(Guid id, CancellationToken cancellationToken)
    {
        await systemAdmins.RequireAsync(cancellationToken);
        return await queries.AuditLogAsync(new LogQueryDto(null, null, null, null, 200), null, id, cancellationToken);
    }

    public Task<WorkspaceDetailDto> SuspendWorkspaceAsync(Guid id, CancellationToken cancellationToken) =>
        ChangeWorkspaceAsync(id, suspend: true, cancellationToken);

    public Task<WorkspaceDetailDto> ReactivateWorkspaceAsync(Guid id, CancellationToken cancellationToken) =>
        ChangeWorkspaceAsync(id, suspend: false, cancellationToken);

    private async Task<WorkspaceDetailDto> ChangeWorkspaceAsync(Guid id, bool suspend, CancellationToken cancellationToken)
    {
        await systemAdmins.RequireAsync(cancellationToken);
        var organization = await organizations.GetAsync(id, cancellationToken) ?? throw NotFound("Workspace");
        if (suspend) organization.Suspend(Now);
        else organization.Reactivate(Now);
        audit.Record(suspend ? PlatformAuditActions.WorkspaceSuspended : PlatformAuditActions.WorkspaceReactivated, "Workspace", id.ToString(),
            organization.Name, organizationId: id);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await GetWorkspaceAsync(id, cancellationToken);
    }

    // ---- Users ----

    public async Task<IReadOnlyList<UserListItemDto>> ListUsersAsync(string? search, bool? active, Guid? organizationId, DateTime? createdFromUtc, CancellationToken cancellationToken)
    {
        await systemAdmins.RequireAsync(cancellationToken);
        return await queries.ListUsersAsync(search, active, organizationId, createdFromUtc, cancellationToken);
    }

    public async Task<UserDetailDto> GetUserAsync(Guid id, CancellationToken cancellationToken)
    {
        await systemAdmins.RequireAsync(cancellationToken);
        var user = await queries.GetUserAsync(id, cancellationToken) ?? throw NotFound("User");
        var signIns = await queries.SignInLogAsync(new LogQueryDto(null, null, null, null, 20), null, id, null, cancellationToken);
        var auditEntries = await queries.AuditLogAsync(new LogQueryDto(null, null, null, null, 20), id, null, cancellationToken);
        return new UserDetailDto(user, signIns, auditEntries);
    }

    public Task<UserDetailDto> DisableUserAsync(Guid id, CancellationToken cancellationToken) => ChangeUserAsync(id, disable: true, cancellationToken);

    public Task<UserDetailDto> EnableUserAsync(Guid id, CancellationToken cancellationToken) => ChangeUserAsync(id, disable: false, cancellationToken);

    private async Task<UserDetailDto> ChangeUserAsync(Guid id, bool disable, CancellationToken cancellationToken)
    {
        await systemAdmins.RequireAsync(cancellationToken);
        if (disable && id == currentUser.UserId)
            throw new BusinessException(ErrorCodes.InvalidStateTransition, "You cannot disable your own account.");
        var user = await users.GetForUpdateAsync(id, cancellationToken) ?? throw NotFound("User");
        if (disable) user.Disable();
        else user.Enable();
        audit.Record(disable ? PlatformAuditActions.UserDisabled : PlatformAuditActions.UserEnabled, "User", id.ToString(), user.Email);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await GetUserAsync(id, cancellationToken);
    }

    // ---- Reconciliation (uncertain outcomes only; nothing is ever re-sent from here) ----

    public async Task<ReconciliationCheckDto> CheckReconciliationAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await systemAdmins.RequireAsync(cancellationToken);
        var detail = await queries.GetPublicationAsync(jobId, cancellationToken) ?? throw NotFound("Publication");
        var (publication, job, attempts) = detail;
        var last = attempts.LastOrDefault();
        var externalId = job.ExternalPostId ?? attempts.LastOrDefault(a => a.ExternalId is not null)?.ExternalId;

        // ponytail: no provider post lookup API is wired yet; the verdict comes from recorded provider evidence.
        // Add ISocialPublisher.FindPostAsync per provider to look the post up live.
        var (verdict, summary) = externalId is not null
            ? ("LIKELY_PUBLISHED", $"The provider returned post id {externalId}. Open the destination to confirm it is live, then Confirm Published.")
            : job.DispatchStartedAtUtc is null
                ? ("NOT_SENT", "The publishing call never started, so nothing was sent. Confirm Failed; the customer can schedule again.")
                : ("UNKNOWN", $"The request was sent to {publication.Platform} but no confirmation came back. Check {publication.Destination} on {publication.Platform} manually.");

        audit.Record(PlatformAuditActions.ReconciliationChecked, "Publication", jobId.ToString(), $"{publication.Platform} · {publication.WorkspaceName}",
            organizationId: publication.OrganizationId, metadata: new Dictionary<string, string?> { ["verdict"] = verdict });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new ReconciliationCheckDto(verdict, summary, externalId, job.ExternalPostUrl, last?.ProviderRequestId);
    }

    public async Task ConfirmPublishedAsync(Guid jobId, string? externalPostId, string? externalPostUrl, CancellationToken cancellationToken)
    {
        await systemAdmins.RequireAsync(cancellationToken);
        var job = await publications.GetAsync(jobId, cancellationToken) ?? throw NotFound("Publication");
        job.ConfirmPublished(externalPostId, externalPostUrl, Now);
        Resolved(job, published: true);
        audit.Record(PlatformAuditActions.ReconciliationConfirmedPublished, "Publication", jobId.ToString(), job.Platform.ToString(),
            organizationId: job.OrganizationId, metadata: new Dictionary<string, string?> { ["externalPostId"] = job.ExternalPostId });
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task ConfirmFailedAsync(Guid jobId, string reason, CancellationToken cancellationToken)
    {
        await systemAdmins.RequireAsync(cancellationToken);
        var job = await publications.GetAsync(jobId, cancellationToken) ?? throw NotFound("Publication");
        job.ConfirmFailed(reason, Now);
        Resolved(job, published: false);
        audit.Record(PlatformAuditActions.ReconciliationConfirmedFailed, "Publication", jobId.ToString(), job.Platform.ToString(),
            organizationId: job.OrganizationId, metadata: new Dictionary<string, string?> { ["reason"] = reason });
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// The workspace sees the outcome like any publication result. The actor is left empty there: platform operators are not
    /// workspace members; the platform audit names them.
    /// </summary>
    private void Resolved(PublicationJob job, bool published)
    {
        activity.Audit(job.OrganizationId, null, published ? AuditActions.PublicationPublished : AuditActions.PublicationFailed, PublicationService.JobResource,
            job.Id, new Dictionary<string, string?> { ["resolution"] = "manual reconciliation" });
        activity.Notify(job.OrganizationId, [job.CreatedByUserId], published ? NotificationType.PUBLISHED : NotificationType.FAILED,
            published ? "Post published" : "Publishing failed",
            published ? $"Your {job.Platform} post was confirmed as published." : $"Your {job.Platform} post was not published. You can schedule it again.",
            PublicationService.JobResource, job.Id);
    }

    private static BusinessException NotFound(string what) => new(ErrorCodes.NotFound, $"{what} not found.");
}
