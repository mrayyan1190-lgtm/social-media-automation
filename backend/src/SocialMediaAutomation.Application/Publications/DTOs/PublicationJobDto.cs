using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Application.Publications.DTOs;

/// <summary>
/// A publication intent as the UI sees it. <see cref="CanCancel"/>/<see cref="CanReschedule"/> are UX hints only; the
/// commands re-check everything.
/// </summary>
public sealed record PublicationJobDto(
    Guid Id,
    Guid PostId,
    Guid PostVersionId,
    int VersionNumber,
    Guid? CampaignId,
    string? CampaignName,
    SocialPlatform Platform,
    Guid SocialAccountId,
    string DestinationName,
    ExecutionMode ExecutionMode,
    DeliveryState DeliveryState,
    DateTime PublishAtUtc,
    DateTime? LocalPublishAt,
    string? TimeZoneId,
    int? UtcOffsetMinutes,
    string PayloadHash,
    int AttemptCount,
    int MaxAttempts,
    string? ExternalPostId,
    string? ExternalPostUrl,
    string? LastErrorCode,
    string? LastErrorMessage,
    DateTime CreatedAtUtc,
    DateTime? CanceledAtUtc,
    bool CanCancel,
    bool CanReschedule,
    byte[] RowVersion);

/// <summary>Filters for the publication list and calendar. Dates filter on PublishAtUtc (inclusive from, exclusive to).</summary>
public sealed record PublicationQuery(
    Guid? PostId = null,
    bool OpenOnly = false,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    SocialPlatform? Platform = null,
    IReadOnlyCollection<DeliveryState>? States = null,
    bool SoonestFirst = false,
    int Take = 500);

/// <summary>Result of Schedule/Publish Now: <see cref="Created"/> is false when an identical intent already existed.</summary>
public sealed record ScheduleResult(PublicationJobDto Job, bool Created);

/// <summary>Wall-clock input from the browser; the server resolves UTC. <see cref="UtcOffsetMinutes"/> picks one occurrence of an ambiguous DST time.</summary>
public sealed record LocalSchedule(DateOnly Date, TimeOnly Time, string TimeZone, int? UtcOffsetMinutes);

public sealed record PublicationAttemptDto(
    Guid Id,
    int AttemptNumber,
    PublicationAttemptStep Step,
    DateTime StartedAtUtc,
    DateTime? FinishedAtUtc,
    DeliveryState? ResultState,
    PublishErrorCategory? ErrorCategory,
    string? ErrorCode,
    string? ErrorMessage,
    string? ProviderRequestId,
    string? ExternalId,
    string? Detail);
