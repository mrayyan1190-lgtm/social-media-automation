using SocialMediaAutomation.Domain.Common;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Domain.Entities;

/// <summary>
/// One platform-specific social post (PRD §12, §17). Holds editorial state and
/// points at its current immutable <see cref="PostVersion"/>. Delivery state never lives here.
/// </summary>
public sealed class Post
{
    private Post() { }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public SocialPlatform Platform { get; private set; }
    public EditorialState EditorialState { get; private set; }

    /// <summary>Always set after creation; nullable only so the post and its first version can be inserted in one transaction.</summary>
    public Guid? CurrentVersionId { get; private set; }

    /// <summary>The approval that authorizes the current version. Cleared whenever a new version is created (PRD §21).</summary>
    public Guid? ActiveApprovalId { get; private set; }

    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    /// <summary>Creates the post and its Version 1 together; the caller persists both in one transaction.</summary>
    public static (Post Post, PostVersion Version) Create(
        Guid id, Guid versionId, Guid organizationId, SocialPlatform platform, PostContent content, IReadOnlyList<MediaAsset> media,
        SocialAccount? destination, PostVersionSource source, Guid userId, DateTime utcNow)
    {
        var post = new Post
        {
            Id = id,
            OrganizationId = organizationId,
            Platform = Guard.Defined(platform, "Platform"),
            EditorialState = EditorialState.DRAFT,
            CreatedByUserId = userId,
            CreatedAtUtc = utcNow,
            UpdatedAtUtc = utcNow,
        };
        var version = PostVersion.Create(versionId, post, versionNumber: 1, content, media, destination, source, userId, utcNow);
        post.CurrentVersionId = version.Id;
        return (post, version);
    }

    /// <summary>
    /// Applies an edit by creating the next immutable version and moving the current pointer to it. Returns null when
    /// nothing public-facing changed. Editing approved content revokes the approval and returns the post to DRAFT.
    /// </summary>
    public PostVersion? Revise(
        Guid versionId, PostVersion current, PostContent content, IReadOnlyList<MediaAsset> media,
        SocialAccount? destination, PostVersionSource source, Guid userId, DateTime utcNow)
    {
        if (current.Id != CurrentVersionId || current.PostId != Id)
            throw new InvalidOperationException("Revise must be based on the post's current version.");
        if (EditorialState is EditorialState.IN_REVIEW or EditorialState.ARCHIVED)
            throw new BusinessException(ErrorCodes.InvalidStateTransition, $"A post in {EditorialState} cannot be edited.");

        var next = PostVersion.Create(versionId, this, current.VersionNumber + 1, content, media, destination, source, userId, utcNow);
        if (next.HasSameContentAs(current))
            return null;

        CurrentVersionId = next.Id;
        ActiveApprovalId = null;
        EditorialState = EditorialState.DRAFT;
        UpdatedAtUtc = utcNow;
        return next;
    }

    /// <summary>DRAFT → IN_REVIEW for the exact current version captured by the review request.</summary>
    public void SubmitForReview(ReviewRequest review, DateTime utcNow)
    {
        if (EditorialState != EditorialState.DRAFT)
            throw new BusinessException(ErrorCodes.InvalidStateTransition,
                EditorialState == EditorialState.CHANGES_REQUESTED
                    ? "Changes were requested: save a new version before resubmitting."
                    : $"A post in {EditorialState} cannot be submitted for review.");
        EnsureReviewOfCurrentVersion(review);
        EditorialState = EditorialState.IN_REVIEW;
        UpdatedAtUtc = utcNow;
    }

    /// <summary>IN_REVIEW → APPROVED, binding the active approval to the decided review of the current version.</summary>
    public void Approve(ReviewRequest review, DateTime utcNow)
    {
        EnsureInReviewOf(review, ReviewStatus.APPROVED);
        EditorialState = EditorialState.APPROVED;
        ActiveApprovalId = review.Id;
        UpdatedAtUtc = utcNow;
    }

    /// <summary>IN_REVIEW → CHANGES_REQUESTED. The reviewed version stays as it is; the creator saves a new one.</summary>
    public void RequestChanges(ReviewRequest review, DateTime utcNow)
    {
        EnsureInReviewOf(review, ReviewStatus.CHANGES_REQUESTED);
        EditorialState = EditorialState.CHANGES_REQUESTED;
        UpdatedAtUtc = utcNow;
    }

    private void EnsureInReviewOf(ReviewRequest review, ReviewStatus decided)
    {
        if (EditorialState != EditorialState.IN_REVIEW)
            throw new BusinessException(ErrorCodes.InvalidStateTransition, $"The post is {EditorialState}, not IN_REVIEW.");
        if (review.Status != decided)
            throw new InvalidOperationException($"The review must be {decided} before the post transitions.");
        EnsureReviewOfCurrentVersion(review);
    }

    private void EnsureReviewOfCurrentVersion(ReviewRequest review)
    {
        if (review.PostId != Id || review.OrganizationId != OrganizationId || review.PostVersionId != CurrentVersionId)
            throw new BusinessException(ErrorCodes.ApprovalStale, "The review does not cover the post's current version.");
    }
}
