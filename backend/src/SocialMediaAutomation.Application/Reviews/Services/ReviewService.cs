using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.Audit.Services;
using SocialMediaAutomation.Application.AiGeneration.Validation;
using SocialMediaAutomation.Application.Common;
using SocialMediaAutomation.Application.Common.Authorization;
using SocialMediaAutomation.Application.Reviews.DTOs;
using SocialMediaAutomation.Application.SocialAccounts.DTOs;
using SocialMediaAutomation.Domain.Common;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Application.Reviews.Services;

/// <summary>
/// Human review of exact versions (PRD §19-21). Every decision is checked on the server: permission, tenant, the exact
/// submitted version still being current, the distinct-reviewer policy and row-version concurrency. Each command
/// commits the review, the post state, the notification and the audit entry in one transaction.
/// </summary>
public sealed class ReviewService(
    OrganizationAuthorizer authorizer,
    IReviewRepository reviews,
    IPostRepository posts,
    ICampaignRepository campaigns,
    IBrandProfileRepository brandProfiles,
    IOrganizationRepository organizations,
    ISocialAccountRepository socialAccounts,
    IMediaRepository media,
    IAiRepository ai,
    ActivityRecorder activity,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    private static readonly OrganizationRole[] Deciders = [OrganizationRole.Reviewer, OrganizationRole.Admin];

    // ---- Submit ---------------------------------------------------------------------------------------------

    public async Task<ReviewSummaryDto> SubmitAsync(Guid organizationId, Guid postId, string? note, byte[]? expectedPostRowVersion, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ManageCampaigns, cancellationToken);
        var post = await LoadPostAsync(access, postId, cancellationToken);
        ConcurrencyGuard.EnsureCurrent(post.RowVersion, expectedPostRowVersion);
        var campaign = await campaigns.GetAsync(post.CampaignId, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.NotFound, "Campaign not found.");
        campaign.EnsureEditable();
        var version = await CurrentVersionAsync(access, post, cancellationToken);

        var blockers = await SubmissionBlockersAsync(access, post, version, cancellationToken);
        if (blockers.Count > 0)
            throw new BusinessException(ErrorCodes.SubmissionBlocked, "Cannot submit for review: " + string.Join(" ", blockers));

        var review = ReviewRequest.Submit(Guid.NewGuid(), post, version, access.UserId, note, Now);
        post.SubmitForReview(review, Now);
        reviews.Add(review);

        activity.Audit(access.OrganizationId, access.UserId, AuditActions.ReviewSubmitted, ActivityRecorder.ReviewResource, review.Id, new Dictionary<string, string?>
        {
            ["postId"] = post.Id.ToString(), ["postVersionId"] = version.Id.ToString(), ["versionNumber"] = version.VersionNumber.ToString(),
            ["platform"] = post.Platform.ToString(),
        });
        activity.Notify(access.OrganizationId, await activity.MembersAsync(access.OrganizationId, Deciders, except: access.UserId, cancellationToken),
            NotificationType.REVIEW_REQUESTED, $"Review requested: {post.Platform} post",
            $"Version {version.VersionNumber} of the {post.Platform} post in \"{campaign.Name}\" is waiting for review.",
            ActivityRecorder.ReviewResource, review.Id);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await SummaryAsync(access, review, cancellationToken);
    }

    /// <summary>Everything that must hold before a version may enter review (PRD §19.1). Returns human-readable reasons.</summary>
    private async Task<List<string>> SubmissionBlockersAsync(OrganizationAccess access, Post post, PostVersion version, CancellationToken cancellationToken)
    {
        var blockers = new List<string>();

        if (version.SocialAccountId is not { } accountId)
            blockers.Add($"Select a {post.Platform} destination account.");
        else if (await socialAccounts.GetAsync(accountId, cancellationToken) is not { } account || account.OrganizationId != access.OrganizationId)
            blockers.Add("The selected destination no longer exists.");
        else if (!account.IsEligibleDestination)
            blockers.Add($"The destination \"{account.DisplayName}\" is {account.ExecutionMode}/{account.ConnectionStatus} and cannot receive content.");

        if (version.MediaAssetId is { } mediaId)
        {
            var image = await media.GetAsync(mediaId, cancellationToken);
            try
            {
                image?.EnsureSuitableFor(post.Platform);
            }
            catch (BusinessException ex)
            {
                blockers.Add(ex.Message);
            }
        }
        else if (PlatformRules.For(post.Platform).RequiresImage)
        {
            blockers.Add($"{post.Platform} posts require one image.");
        }

        var evaluation = (await ai.ListEvaluationsAsync(access.OrganizationId, post.Id, cancellationToken)).FirstOrDefault(e => e.PostVersionId == version.Id);
        var prohibited = (await brandProfiles.GetByOrganizationAsync(access.OrganizationId, cancellationToken))?.ProhibitedTerms ?? [];
        var critical = (evaluation?.Issues ?? []).Concat(DeterministicValidator.SubmissionBlockers(version, prohibited))
            .Where(i => i.Severity == IssueSeverity.CRITICAL)
            .Select(i => i.Message)
            .Distinct();
        blockers.AddRange(critical.Select(message => $"Critical issue: {message}"));
        return blockers;
    }

    // ---- Decide ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Approves exactly the submitted version and binds the post's active approval to it, together with the canonical
    /// payload hash, the notification and the audit entry, in one transaction.
    /// </summary>
    public async Task<ReviewSummaryDto> ApproveAsync(Guid organizationId, Guid reviewId, byte[]? expectedRowVersion, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ReviewContent, cancellationToken);
        var (review, post, version) = await LoadDecidableAsync(access, reviewId, expectedRowVersion, cancellationToken);
        var organization = await organizations.GetAsync(access.OrganizationId, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.NotFound, "Organization not found.");

        var account = version.SocialAccountId is { } accountId ? await socialAccounts.GetAsync(accountId, cancellationToken) : null;
        if (account is null || account.OrganizationId != access.OrganizationId || !account.IsEligibleDestination)
            throw new BusinessException(ErrorCodes.SubmissionBlocked, "The version's destination is no longer eligible; request changes instead.");
        var image = version.MediaAssetId is { } mediaId ? await media.GetAsync(mediaId, cancellationToken) : null;

        var payload = ApprovalPayload.From(post, version, account, image);
        review.Approve(access.UserId, version, organization.RequireDistinctReviewer, payload, Now);
        post.Approve(review, Now);

        activity.Audit(access.OrganizationId, access.UserId, AuditActions.ContentApproved, ActivityRecorder.ReviewResource, review.Id, new Dictionary<string, string?>
        {
            ["postId"] = post.Id.ToString(), ["postVersionId"] = version.Id.ToString(), ["versionNumber"] = version.VersionNumber.ToString(),
            ["payloadVersion"] = review.ApprovedPayloadVersion, ["payloadHash"] = review.ApprovedPayloadHash,
            ["requireDistinctReviewer"] = organization.RequireDistinctReviewer.ToString(),
        });
        activity.Notify(access.OrganizationId, [review.SubmittedByUserId, version.CreatedByUserId], NotificationType.APPROVED,
            $"Approved: {post.Platform} post", $"Version {version.VersionNumber} of the {post.Platform} post was approved.",
            ActivityRecorder.ReviewResource, review.Id);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await SummaryAsync(access, review, cancellationToken);
    }

    public async Task<ReviewSummaryDto> RequestChangesAsync(Guid organizationId, Guid reviewId, string? explanation, byte[]? expectedRowVersion, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ReviewContent, cancellationToken);
        var (review, post, version) = await LoadDecidableAsync(access, reviewId, expectedRowVersion, cancellationToken);

        review.RequestChanges(access.UserId, version, explanation ?? "", Now);
        post.RequestChanges(review, Now);

        activity.Audit(access.OrganizationId, access.UserId, AuditActions.ChangesRequested, ActivityRecorder.ReviewResource, review.Id, new Dictionary<string, string?>
        {
            ["postId"] = post.Id.ToString(), ["postVersionId"] = version.Id.ToString(), ["versionNumber"] = version.VersionNumber.ToString(),
        });
        activity.Notify(access.OrganizationId, [review.SubmittedByUserId, version.CreatedByUserId], NotificationType.CHANGES_REQUESTED,
            $"Changes requested: {post.Platform} post", review.DecisionComment!, ActivityRecorder.ReviewResource, review.Id);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await SummaryAsync(access, review, cancellationToken);
    }

    public async Task<ReviewCommentDto> AddCommentAsync(Guid organizationId, Guid reviewId, string? body, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.CommentOnReviews, cancellationToken);
        var review = await LoadReviewAsync(access, reviewId, cancellationToken);
        var comment = ReviewComment.Create(review, access.UserId, body ?? "", Now);
        reviews.Add(comment);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return (await reviews.ListCommentsAsync(access.OrganizationId, review.Id, cancellationToken)).Single(c => c.Id == comment.Id);
    }

    // ---- Read -----------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<ReviewSummaryDto>> ListAsync(Guid organizationId, ReviewStatus? status, Guid? postId, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ViewCampaigns, cancellationToken);
        return await reviews.ListAsync(access.OrganizationId, status, postId, cancellationToken);
    }

    public async Task<ReviewDetailDto> GetDetailAsync(Guid organizationId, Guid reviewId, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ViewCampaigns, cancellationToken);
        var review = await LoadReviewAsync(access, reviewId, cancellationToken);
        var post = await LoadPostAsync(access, review.PostId, cancellationToken);
        var campaign = await campaigns.GetAsync(post.CampaignId, cancellationToken) ?? throw new BusinessException(ErrorCodes.NotFound, "Campaign not found.");

        var version = (await posts.ListVersionsAsync(access.OrganizationId, post.Id, cancellationToken)).Single(v => v.Id == review.PostVersionId);
        var destination = version.SocialAccountId is { } accountId && await socialAccounts.GetAsync(accountId, cancellationToken) is { } account
            && account.OrganizationId == access.OrganizationId ? SocialAccountDto.From(account) : null;
        var history = await reviews.ListAsync(access.OrganizationId, null, post.Id, cancellationToken);
        var (canDecide, reason) = await CanDecideAsync(access, review, post, version.CreatedByUserId, cancellationToken);

        return new ReviewDetailDto(
            history.Single(r => r.Id == review.Id),
            post.EditorialState,
            post.CurrentVersionId!.Value,
            post.CurrentVersionId == review.PostVersionId,
            version,
            destination,
            new CampaignIntentDto(campaign.Id, campaign.Name, campaign.Objective, campaign.Description, campaign.TargetAudience,
                campaign.CallToAction, campaign.CallToActionUrl),
            await campaigns.ListFactsAsync(access.OrganizationId, campaign.Id, cancellationToken),
            (await ai.ListEvaluationsAsync(access.OrganizationId, post.Id, cancellationToken)).FirstOrDefault(e => e.PostVersionId == review.PostVersionId),
            await reviews.ListCommentsAsync(access.OrganizationId, review.Id, cancellationToken),
            history,
            canDecide,
            reason);
    }

    /// <summary>UX hint for the review screen. The commands re-check everything; this never grants anything.</summary>
    private async Task<(bool, string?)> CanDecideAsync(OrganizationAccess access, ReviewRequest review, Post post, Guid versionCreator, CancellationToken cancellationToken)
    {
        if (!RolePermissions.Has(access.Role, Permission.ReviewContent))
            return (false, "Your role cannot decide reviews.");
        if (review.Status != ReviewStatus.PENDING)
            return (false, $"This review is already {review.Status}.");
        if (post.CurrentVersionId != review.PostVersionId || post.EditorialState != EditorialState.IN_REVIEW)
            return (false, "The post has changed since this version was submitted.");
        var organization = await organizations.GetAsync(access.OrganizationId, cancellationToken);
        if (organization?.RequireDistinctReviewer == true && (access.UserId == review.SubmittedByUserId || access.UserId == versionCreator))
            return (false, "You submitted or last edited this version; another reviewer must decide (distinct reviewer policy).");
        return (true, null);
    }

    // ---- Helpers --------------------------------------------------------------------------------------------

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    /// <summary>Loads a pending review whose exact version is still the post's current IN_REVIEW version.</summary>
    private async Task<(ReviewRequest Review, Post Post, PostVersion Version)> LoadDecidableAsync(
        OrganizationAccess access, Guid reviewId, byte[]? expectedRowVersion, CancellationToken cancellationToken)
    {
        var review = await LoadReviewAsync(access, reviewId, cancellationToken);
        ConcurrencyGuard.EnsureCurrent(review.RowVersion, expectedRowVersion);
        var post = await LoadPostAsync(access, review.PostId, cancellationToken);
        if (review.Status == ReviewStatus.PENDING && (post.CurrentVersionId != review.PostVersionId || post.EditorialState != EditorialState.IN_REVIEW))
            throw new BusinessException(ErrorCodes.ApprovalStale, "The post no longer shows the submitted version; this review cannot be decided.");
        var version = await posts.GetVersionAsync(access.OrganizationId, post.Id, review.PostVersionId, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.NotFound, "Version not found.");
        return (review, post, version);
    }

    private async Task<ReviewRequest> LoadReviewAsync(OrganizationAccess access, Guid reviewId, CancellationToken cancellationToken)
    {
        var review = await reviews.GetAsync(reviewId, cancellationToken) ?? throw new BusinessException(ErrorCodes.NotFound, "Review not found.");
        access.EnsureOwns(review.OrganizationId, "Review");
        return review;
    }

    private async Task<Post> LoadPostAsync(OrganizationAccess access, Guid postId, CancellationToken cancellationToken)
    {
        var post = await posts.GetAsync(postId, cancellationToken) ?? throw new BusinessException(ErrorCodes.NotFound, "Post not found.");
        access.EnsureOwns(post.OrganizationId, "Post");
        return post;
    }

    private async Task<PostVersion> CurrentVersionAsync(OrganizationAccess access, Post post, CancellationToken cancellationToken) =>
        await posts.GetVersionAsync(access.OrganizationId, post.Id, post.CurrentVersionId!.Value, cancellationToken)
        ?? throw new InvalidOperationException($"Post {post.Id} has no current version row.");

    private async Task<ReviewSummaryDto> SummaryAsync(OrganizationAccess access, ReviewRequest review, CancellationToken cancellationToken) =>
        (await reviews.ListAsync(access.OrganizationId, null, review.PostId, cancellationToken)).Single(r => r.Id == review.Id);
}
