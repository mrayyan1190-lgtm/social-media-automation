using SocialMediaAutomation.Domain.Common;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Domain.Entities;

/// <summary>
/// One human review of one exact, immutable PostVersion (PRD §19). An approval records the canonical payload and its
/// hash, so later steps can prove that what is scheduled or dispatched is exactly what was approved.
/// </summary>
public sealed class ReviewRequest
{
    public const int NoteMax = 2000;

    private ReviewRequest() { }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid PostId { get; private set; }
    public Guid PostVersionId { get; private set; }
    public int VersionNumber { get; private set; }
    public Guid SubmittedByUserId { get; private set; }

    /// <summary>The team member asked to review. Routing only: any Reviewer/Admin may still decide (role and distinct-reviewer rules apply).</summary>
    public Guid? AssignedReviewerUserId { get; private set; }

    public string? SubmissionNote { get; private set; }
    public DateTime SubmittedAtUtc { get; private set; }
    public ReviewStatus Status { get; private set; }
    public Guid? DecidedByUserId { get; private set; }
    public DateTime? DecidedAtUtc { get; private set; }
    public string? DecisionComment { get; private set; }
    public string? ApprovedPayloadVersion { get; private set; }
    public string? ApprovedPayloadJson { get; private set; }
    public string? ApprovedPayloadHash { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static ReviewRequest Submit(Guid id, Post post, PostVersion version, Guid userId, string? note, Guid? assignedReviewerUserId, DateTime utcNow)
    {
        if (version.PostId != post.Id || version.Id != post.CurrentVersionId)
            throw new BusinessException(ErrorCodes.ApprovalStale, "Only the post's current version can be submitted for review.");
        return new ReviewRequest
        {
            Id = id,
            OrganizationId = post.OrganizationId,
            PostId = post.Id,
            PostVersionId = version.Id,
            VersionNumber = version.VersionNumber,
            SubmittedByUserId = userId,
            AssignedReviewerUserId = assignedReviewerUserId,
            SubmissionNote = Guard.Optional(note, "Submission note", NoteMax),
            SubmittedAtUtc = utcNow,
            Status = ReviewStatus.PENDING,
        };
    }

    /// <summary>
    /// Records an approval of exactly <paramref name="version"/>. With RequireDistinctReviewer, neither the submitter
    /// nor the version's last human editor may approve it (PRD §19.4); this applies to Admins too.
    /// </summary>
    public void Approve(Guid reviewerUserId, PostVersion version, bool requireDistinctReviewer, ApprovalPayload payload, DateTime utcNow)
    {
        EnsureDecidable(version);
        if (requireDistinctReviewer && (reviewerUserId == SubmittedByUserId || reviewerUserId == version.CreatedByUserId))
            throw new BusinessException(ErrorCodes.DistinctReviewerRequired,
                "This workspace requires a distinct reviewer: the submitter or last editor of a version cannot approve it.");

        Status = ReviewStatus.APPROVED;
        DecidedByUserId = reviewerUserId;
        DecidedAtUtc = utcNow;
        ApprovedPayloadVersion = payload.PayloadVersion;
        ApprovedPayloadJson = payload.ToCanonicalJson();
        ApprovedPayloadHash = payload.ComputeHash();
    }

    public void RequestChanges(Guid reviewerUserId, PostVersion version, string comment, DateTime utcNow)
    {
        EnsureDecidable(version);
        DecisionComment = Guard.Required(comment, "Explanation", NoteMax);
        Status = ReviewStatus.CHANGES_REQUESTED;
        DecidedByUserId = reviewerUserId;
        DecidedAtUtc = utcNow;
    }

    private void EnsureDecidable(PostVersion version)
    {
        if (Status != ReviewStatus.PENDING)
            throw new BusinessException(ErrorCodes.InvalidStateTransition, $"This review was already decided ({Status}).");
        if (version.Id != PostVersionId)
            throw new BusinessException(ErrorCodes.ApprovalStale, "The decision must be about the exact submitted version.");
    }
}

/// <summary>An append-only remark in a review thread.</summary>
public sealed class ReviewComment
{
    public const int BodyMax = 2000;

    private ReviewComment() { }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid ReviewRequestId { get; private set; }
    public Guid AuthorUserId { get; private set; }
    public string Body { get; private set; } = null!;
    public DateTime CreatedAtUtc { get; private set; }

    public static ReviewComment Create(ReviewRequest review, Guid authorUserId, string body, DateTime utcNow) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = review.OrganizationId,
        ReviewRequestId = review.Id,
        AuthorUserId = authorUserId,
        Body = Guard.Required(body, "Comment", BodyMax),
        CreatedAtUtc = utcNow,
    };
}
