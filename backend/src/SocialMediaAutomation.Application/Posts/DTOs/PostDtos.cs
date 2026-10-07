using SocialMediaAutomation.Application.Media.DTOs;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Application.Posts.DTOs;

public sealed record PostVersionDto(
    Guid Id,
    Guid PostId,
    int VersionNumber,
    string? Hook,
    string Body,
    string? CallToAction,
    string? DestinationUrl,
    IReadOnlyList<string> Hashtags,
    Guid? SocialAccountId,
    Guid? MediaAssetId,
    string? AltText,
    PostVersionSource CreationSource,
    Guid CreatedByUserId,
    string CreatedByName,
    DateTime CreatedAtUtc,
    MediaAssetDto? Media);

public sealed record PostDto(
    Guid Id,
    Guid OrganizationId,
    Guid? CampaignId,
    SocialPlatform Platform,
    EditorialState EditorialState,
    Guid CurrentVersionId,
    Guid? ActiveApprovalId,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    byte[] RowVersion,
    PostVersionDto CurrentVersion);

/// <summary>
/// One row of the post list (Planner, Campaign detail, Home). The latest publication job is carried separately from the
/// editorial state so the two are never merged on the server.
/// </summary>
public sealed record PostListItemDto(
    Guid Id,
    Guid? CampaignId,
    string? CampaignName,
    string? CampaignColor,
    SocialPlatform Platform,
    EditorialState EditorialState,
    int CurrentVersionNumber,
    string? Hook,
    string BodyExcerpt,
    Guid? SocialAccountId,
    Guid CreatedByUserId,
    string CreatedByName,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    Guid? LatestJobId,
    DeliveryState? LatestDeliveryState,
    DateTime? LatestPublishAtUtc,
    Guid? MediaAssetId = null);
