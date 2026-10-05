using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Application.Platform;

// Read models for the internal System Admin area. Operational metadata only: no customer post content, no prompts,
// no secrets, tokens or password hashes.

// ---- Workspaces ----

public sealed record WorkspaceListItemDto(
    Guid Id, string Name, string? PrimaryAdminName, string? PrimaryAdminEmail, int Members, int Campaigns, int SocialAccounts,
    OrganizationStatus Status, DateTime CreatedAtUtc, DateTime? LastActivityAtUtc);

public sealed record WorkspaceUsageDto(int Members, int Campaigns, int Posts, int ConnectedSocialAccounts, int AiRequests, int PublishedPosts);

public sealed record WorkspaceMemberDto(Guid UserId, string Name, string Email, OrganizationRole Role, MembershipStatus Status);

/// <summary>Status is customer wording: Connected, Needs Reconnection, Disconnected or Demo Mode.</summary>
public sealed record WorkspaceConnectionDto(SocialPlatform Platform, string DisplayName, string Status);

public sealed record WorkspaceDetailDto(
    Guid Id, string Name, string TimeZoneId, DateTime CreatedAtUtc, OrganizationStatus Status, WorkspaceUsageDto Usage,
    IReadOnlyList<WorkspaceMemberDto> Members, IReadOnlyList<WorkspaceConnectionDto> Connections);

// ---- Users ----

public sealed record UserMembershipDto(Guid OrganizationId, string OrganizationName, OrganizationRole Role, MembershipStatus Status);

public sealed record UserListItemDto(
    Guid Id, string Name, string Email, IReadOnlyList<UserMembershipDto> Workspaces, bool IsActive, bool IsSystemAdmin, DateTime CreatedAtUtc,
    DateTime? LastLoginAtUtc);

public sealed record UserDetailDto(UserListItemDto User, IReadOnlyList<SignInLogItemDto> RecentSignIns, IReadOnlyList<AuditLogItemDto> RecentAudit);

// ---- Workers & queues ----

/// <summary>Status: HEALTHY, STALE or OFFLINE.</summary>
public sealed record WorkerDto(
    string WorkerId, WorkerKind Kind, string Instance, string Status, DateTime LastSeenAtUtc, int CurrentJobs, DateTime StartedAtUtc,
    long UptimeSeconds, string? Version);

public sealed record AiQueueSummaryItemsDto(int Pending, int Processing, int RetryPending, int FailedLast24Hours, DateTime? OldestPendingAtUtc);

public sealed record AiQueueItemDto(
    Guid Id, Guid OrganizationId, string WorkspaceName, string Operation, string Status, int Attempts, int MaxAttempts, DateTime CreatedAtUtc,
    DateTime? StartedAtUtc, int? DurationMs, string? Error, string? CorrelationId);

public sealed record AiQueueViewDto(AiQueueSummaryItemsDto Summary, IReadOnlyList<AiQueueItemDto> Items);

public sealed record PublishingQueueSummaryItemsDto(
    int Scheduled, int Processing, int WaitingMedia, int RetryPending, int ActionRequired, int ReconciliationRequired, int FailedLast24Hours);

public sealed record PublishingQueueViewDto(PublishingQueueSummaryItemsDto Summary, IReadOnlyList<PublicationListItemDto> Items);

// ---- Publications & reconciliation ----

public sealed record PublicationQueryDto(Guid? OrganizationId, SocialPlatform? Platform, DateTime? FromUtc, DateTime? ToUtc, IReadOnlyList<DeliveryState>? States);

public sealed record PublicationListItemDto(
    Guid Id, Guid OrganizationId, string WorkspaceName, Guid CampaignId, string CampaignName, Guid PostId, int VersionNumber, SocialPlatform Platform,
    string Destination, ExecutionMode ExecutionMode, DateTime PublishAtUtc, DeliveryState State, int Attempts, int MaxAttempts, string? Worker,
    string? LastErrorCode, string? LastErrorMessage, string? CorrelationId, DateTime CreatedAtUtc);

public sealed record PublicationAttemptAdminDto(
    Guid Id, int AttemptNumber, PublicationAttemptStep Step, string WorkerId, DateTime StartedAtUtc, DateTime? FinishedAtUtc, DeliveryState? ResultState,
    PublishErrorCategory? ErrorCategory, string? ErrorCode, string? ErrorMessage, string? ProviderRequestId, string? ExternalId, string? Detail);

public sealed record TimelineEventDto(DateTime AtUtc, string Title, string? Detail, string Tone);

public sealed record PublicationDetailDto(
    PublicationListItemDto Publication, string PayloadHash, string? ExternalPostId, string? ExternalPostUrl, DateTime? DispatchStartedAtUtc,
    DateTime? CompletedAtUtc, DateTime? CanceledAtUtc, IReadOnlyList<PublicationAttemptAdminDto> Attempts, IReadOnlyList<TimelineEventDto> Timeline);

/// <summary>Result of "Check Provider Status": what the system knows, never a blind resend.</summary>
public sealed record ReconciliationCheckDto(string Verdict, string Summary, string? ExternalPostId, string? ExternalPostUrl, string? ProviderRequestId);

// ---- Logs ----

public sealed record LogQueryDto(DateTime? FromUtc, DateTime? ToUtc, string? Search, string? CorrelationId, int Take = 200);

/// <summary>Scope: PLATFORM (operator action) or WORKSPACE (customer business action).</summary>
public sealed record AuditLogItemDto(
    Guid Id, DateTime AtUtc, string? Actor, string Action, string TargetType, string Target, string? Workspace, string Result, string? CorrelationId,
    string Scope);

public sealed record SignInLogItemDto(
    Guid Id, DateTime AtUtc, Guid? UserId, string? UserName, string Email, string Area, string EventType, bool Succeeded, string? IpAddress,
    string? UserAgent, string? CorrelationId);

public sealed record AiLogItemDto(
    Guid Id, Guid RequestId, string Workspace, string Operation, string Model, bool Succeeded, DateTime StartedAtUtc, int DurationMs,
    int? InputTokens, int? OutputTokens, int Attempts, string? Error, string? CorrelationId);

public sealed record PublishingLogItemDto(
    Guid Id, DateTime AtUtc, Guid PublicationId, string Workspace, SocialPlatform Platform, string Operation, string Status,
    string? ProviderRequestId, int? DurationMs, int? Attempt, string? ErrorCategory, string? Error, string? CorrelationId);

public sealed record SystemLogItemDto(Guid Id, DateTime AtUtc, string Level, string Service, string Event, string Message, string? CorrelationId);

// ---- Provider / AI activity used by configuration status and health ----

public sealed record AiActivityDto(DateTime? LastSuccessAtUtc, DateTime? LastFailureAtUtc, string? LastFailure, int RequestsToday, int FailedToday,
    int CallsLastHour, int FailedCallsLastHour, int? AverageLatencyMsLastHour);

public sealed record ProviderActivityDto(DateTime? LastSuccessAtUtc, DateTime? LastFailureAtUtc, string? LastFailure, int FailuresLast24Hours, int SuccessesLast24Hours);

/// <summary>All read queries of the internal area, implemented in Infrastructure.</summary>
public interface IInternalQueries
{
    Task<IReadOnlyList<WorkspaceListItemDto>> ListWorkspacesAsync(string? search, OrganizationStatus? status, DateTime? createdFromUtc, CancellationToken cancellationToken);

    Task<WorkspaceDetailDto?> GetWorkspaceAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<UserListItemDto>> ListUsersAsync(string? search, bool? active, Guid? organizationId, DateTime? createdFromUtc, CancellationToken cancellationToken);

    Task<UserListItemDto?> GetUserAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, int>> CurrentJobsByWorkerAsync(CancellationToken cancellationToken);

    Task<AiQueueViewDto> AiQueueAsync(DateTime utcNow, CancellationToken cancellationToken);

    Task<PublishingQueueViewDto> PublishingQueueAsync(DateTime utcNow, CancellationToken cancellationToken);

    Task<IReadOnlyList<PublicationListItemDto>> ListPublicationsAsync(PublicationQueryDto query, CancellationToken cancellationToken);

    Task<(PublicationListItemDto Publication, PublicationJob Job, IReadOnlyList<PublicationAttemptAdminDto> Attempts)?> GetPublicationAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<AuditLogItemDto>> AuditLogAsync(LogQueryDto query, Guid? actorUserId, Guid? organizationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SignInLogItemDto>> SignInLogAsync(LogQueryDto query, bool? succeeded, Guid? userId, string? ipAddress, CancellationToken cancellationToken);

    Task<IReadOnlyList<AiLogItemDto>> AiLogAsync(LogQueryDto query, CancellationToken cancellationToken);

    Task<IReadOnlyList<PublishingLogItemDto>> PublishingLogAsync(LogQueryDto query, CancellationToken cancellationToken);

    Task<IReadOnlyList<SystemLogItemDto>> SystemLogAsync(LogQueryDto query, string? level, string? service, CancellationToken cancellationToken);

    /// <param name="images">True for image generation activity; false for text (generation, rewrite, evaluation).</param>
    Task<AiActivityDto> AiActivityAsync(DateTime utcNow, CancellationToken cancellationToken, bool images = false);

    Task<ProviderActivityDto> ProviderActivityAsync(IReadOnlyCollection<SocialPlatform> platforms, DateTime utcNow, CancellationToken cancellationToken);

    Task<int> DatabaseLatencyMsAsync(CancellationToken cancellationToken);
}
