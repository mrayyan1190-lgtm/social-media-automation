using SocialMediaAutomation.Application.AiGeneration.DTOs;
using SocialMediaAutomation.Application.Campaigns.DTOs;
using SocialMediaAutomation.Application.Posts.DTOs;
using SocialMediaAutomation.Application.SocialAccounts.DTOs;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Application.Reviews.DTOs;

public sealed record ReviewSummaryDto(
    Guid Id,
    Guid PostId,
    Guid PostVersionId,
    int VersionNumber,
    SocialPlatform Platform,
    Guid CampaignId,
    string CampaignName,
    ReviewStatus Status,
    Guid SubmittedByUserId,
    string SubmittedByName,
    string? SubmissionNote,
    DateTime SubmittedAtUtc,
    Guid? DecidedByUserId,
    string? DecidedByName,
    DateTime? DecidedAtUtc,
    string? DecisionComment,
    string? ApprovedPayloadHash,
    byte[] RowVersion);

public sealed record ReviewCommentDto(Guid Id, Guid ReviewRequestId, Guid AuthorUserId, string AuthorName, string Body, DateTime CreatedAtUtc);

/// <summary>The campaign intent a reviewer needs to judge the post (PRD §19.2).</summary>
public sealed record CampaignIntentDto(
    Guid Id, string Name, CampaignObjective Objective, string Description, string TargetAudience, CallToActionType CallToAction, string? CallToActionUrl);

/// <summary>
/// Everything needed to review one exact version: the submitted content (never the post's newer current content),
/// destination, media, campaign intent, facts, automated findings, comments and the post's review history.
/// </summary>
public sealed record ReviewDetailDto(
    ReviewSummaryDto Review,
    EditorialState PostState,
    Guid PostCurrentVersionId,
    bool IsCurrentVersion,
    PostVersionDto Version,
    SocialAccountDto? Destination,
    CampaignIntentDto Campaign,
    IReadOnlyList<CampaignFactDto> Facts,
    ContentEvaluationDto? Evaluation,
    IReadOnlyList<ReviewCommentDto> Comments,
    IReadOnlyList<ReviewSummaryDto> History,
    bool CanDecide,
    string? CannotDecideReason);
