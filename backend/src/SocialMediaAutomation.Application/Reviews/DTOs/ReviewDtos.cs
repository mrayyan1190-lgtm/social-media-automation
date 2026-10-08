using SocialMediaAutomation.Application.AiGeneration.DTOs;
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
    /// <summary>Hook or the start of the body of the reviewed version, for lists.</summary>
    string Excerpt,
    ReviewStatus Status,
    Guid SubmittedByUserId,
    string SubmittedByName,
    Guid? AssignedReviewerUserId,
    string? AssignedReviewerName,
    string? SubmissionNote,
    DateTime SubmittedAtUtc,
    Guid? DecidedByUserId,
    string? DecidedByName,
    DateTime? DecidedAtUtc,
    string? DecisionComment,
    string? ApprovedPayloadHash,
    byte[] RowVersion);

public sealed record ReviewCommentDto(Guid Id, Guid ReviewRequestId, Guid AuthorUserId, string AuthorName, string Body, DateTime CreatedAtUtc);

/// <summary>
/// Everything needed to review one exact version: the submitted content (never the post's newer current content),
/// destination, media, automated findings, comments and the post's review history.
/// </summary>
public sealed record ReviewDetailDto(
    ReviewSummaryDto Review,
    EditorialState PostState,
    Guid PostCurrentVersionId,
    bool IsCurrentVersion,
    PostVersionDto Version,
    SocialAccountDto? Destination,
    ContentEvaluationDto? Evaluation,
    IReadOnlyList<ReviewCommentDto> Comments,
    IReadOnlyList<ReviewSummaryDto> History,
    bool CanDecide,
    string? CannotDecideReason);
