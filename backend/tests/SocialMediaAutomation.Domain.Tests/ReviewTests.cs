using SocialMediaAutomation.Domain.Common;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Domain.Tests;

public class ReviewTests
{
    private static readonly Guid Creator = Guid.NewGuid();
    private static readonly Guid Reviewer = Guid.NewGuid();
    private static readonly DateTime Now = DateTime.UtcNow;

    private static readonly Guid Org = Guid.NewGuid();
    private readonly SocialAccount account;
    private readonly SocialAccount otherAccount;
    private readonly MediaAsset image;

    public ReviewTests()
    {
        account = SocialAccount.CreateUnconnected(Guid.NewGuid(), Org, SocialPlatform.LinkedIn, "LI", ExecutionMode.SIMULATED, Creator, Now);
        otherAccount = SocialAccount.CreateUnconnected(Guid.NewGuid(), Org, SocialPlatform.LinkedIn, "LI 2", ExecutionMode.SIMULATED, Creator, Now);
        image = MediaAsset.CreateUpload(Guid.NewGuid(), Org, "a.png", "image/png", 100, 1200, 628, new string('b', 64), "k/1.png", Creator, Now);
    }

    private PostContent Content(string body = "Close the books faster.", SocialAccount? destination = null, MediaAsset? media = null, string[]? hashtags = null, string? alt = null) =>
        new("Hook", body, "Book a demo", "https://brightline.example/demo", hashtags ?? ["FinanceOps"],
            media is null ? null : [new PostImage(media.Id, alt ?? "Dashboard")], (destination ?? account).Id);

    private (Post Post, PostVersion V1) NewPost() =>
        Post.Create(Guid.NewGuid(), Guid.NewGuid(), Org, SocialPlatform.LinkedIn, Content(), [], account, PostVersionSource.Manual, Creator, Now);

    private static ApprovalPayload Payload(Post post, PostVersion version, SocialAccount destination, MediaAsset? media = null) =>
        ApprovalPayload.From(post, version, destination, media is null ? [] : [media]);

    private (Post Post, PostVersion V1, ReviewRequest Review) Submitted()
    {
        var (post, v1) = NewPost();
        var review = ReviewRequest.Submit(Guid.NewGuid(), post, v1, Creator, null, null, Now);
        post.SubmitForReview(review, Now);
        return (post, v1, review);
    }

    // ---- State machine -------------------------------------------------------------------------------

    [Fact]
    public void Approval_binds_the_exact_submitted_version()
    {
        var (post, v1, review) = Submitted();

        review.Approve(Reviewer, v1, requireDistinctReviewer: true, Payload(post, v1, account), Now);
        post.Approve(review, Now);

        Assert.Equal(EditorialState.APPROVED, post.EditorialState);
        Assert.Equal(review.Id, post.ActiveApprovalId);
        Assert.Equal(v1.Id, review.PostVersionId);
        Assert.Equal(Payload(post, v1, account).ComputeHash(), review.ApprovedPayloadHash);
    }

    [Fact]
    public void Edit_after_approval_creates_a_draft_without_approval_and_keeps_the_approved_version()
    {
        var (post, v1, review) = Submitted();
        review.Approve(Reviewer, v1, true, Payload(post, v1, account), Now);
        post.Approve(review, Now);
        var approvedBody = v1.Body;

        var v2 = post.Revise(Guid.NewGuid(), v1, Content("Edited after approval."), [], account, PostVersionSource.Manual, Creator, Now)!;

        Assert.Equal((EditorialState.DRAFT, (Guid?)null, v2.Id), (post.EditorialState, post.ActiveApprovalId, post.CurrentVersionId));
        Assert.Equal(approvedBody, v1.Body);
        Assert.Equal(ReviewStatus.APPROVED, review.Status); // the historical approval of v1 is preserved
        Assert.Throws<BusinessException>(() => post.Approve(review, Now)); // and can never be re-attached to v2
    }

    [Fact]
    public void Changes_requested_needs_a_new_version_before_resubmission()
    {
        var (post, v1, review) = Submitted();

        Assert.Equal(ErrorCodes.ValidationFailed, Assert.Throws<BusinessException>(() => review.RequestChanges(Reviewer, v1, "  ", Now)).Code);
        review.RequestChanges(Reviewer, v1, "Remove the unsupported statistic.", Now);
        post.RequestChanges(review, Now);
        Assert.Equal(EditorialState.CHANGES_REQUESTED, post.EditorialState);

        var resubmitSameVersion = ReviewRequest.Submit(Guid.NewGuid(), post, v1, Creator, null, null, Now);
        Assert.Equal(ErrorCodes.InvalidStateTransition, Assert.Throws<BusinessException>(() => post.SubmitForReview(resubmitSameVersion, Now)).Code);

        var v2 = post.Revise(Guid.NewGuid(), v1, Content("Revised."), [], account, PostVersionSource.Manual, Creator, Now)!;
        post.SubmitForReview(ReviewRequest.Submit(Guid.NewGuid(), post, v2, Creator, null, null, Now), Now);
        Assert.Equal(EditorialState.IN_REVIEW, post.EditorialState);
    }

    [Fact]
    public void Content_in_review_cannot_be_edited_and_a_review_is_decided_once()
    {
        var (post, v1, review) = Submitted();

        Assert.Throws<BusinessException>(() => post.Revise(Guid.NewGuid(), v1, Content("x"), [], account, PostVersionSource.Manual, Creator, Now));
        review.Approve(Reviewer, v1, true, Payload(post, v1, account), Now);
        Assert.Equal(ErrorCodes.InvalidStateTransition,
            Assert.Throws<BusinessException>(() => review.RequestChanges(Reviewer, v1, "Too late", Now)).Code);
    }

    [Fact]
    public void A_review_can_only_decide_its_own_version()
    {
        var (post, v1, review) = Submitted();
        var (_, otherV1) = NewPost();

        Assert.Equal(ErrorCodes.ApprovalStale,
            Assert.Throws<BusinessException>(() => review.Approve(Reviewer, otherV1, true, Payload(post, v1, account), Now)).Code);
    }

    // ---- Distinct reviewer -------------------------------------------------------------------------------

    [Fact]
    public void Submitter_cannot_approve_when_distinct_reviewer_is_required()
    {
        var (post, v1, review) = Submitted();

        var error = Assert.Throws<BusinessException>(() => review.Approve(Creator, v1, requireDistinctReviewer: true, Payload(post, v1, account), Now));

        Assert.Equal(ErrorCodes.DistinctReviewerRequired, error.Code);
        Assert.Equal(ReviewStatus.PENDING, review.Status);
    }

    [Fact]
    public void Last_editor_cannot_approve_even_if_someone_else_submitted()
    {
        var (post, v1) = NewPost(); // created by Creator
        var review = ReviewRequest.Submit(Guid.NewGuid(), post, v1, Reviewer, null, null, Now); // submitted by someone else

        Assert.Equal(ErrorCodes.DistinctReviewerRequired,
            Assert.Throws<BusinessException>(() => review.Approve(Creator, v1, true, Payload(post, v1, account), Now)).Code);
    }

    [Fact]
    public void Self_approval_is_allowed_only_when_the_policy_is_off()
    {
        var (post, v1, review) = Submitted();

        review.Approve(Creator, v1, requireDistinctReviewer: false, Payload(post, v1, account), Now);

        Assert.Equal(ReviewStatus.APPROVED, review.Status);
    }

    // ---- Canonical payload -------------------------------------------------------------------------------

    [Fact]
    public void Same_payload_gives_same_hash_and_no_url_is_part_of_media_identity()
    {
        var (post, v1) = NewPost();
        var a = Payload(post, v1, account);
        var b = Payload(post, v1, account);

        Assert.Equal(a.ComputeHash(), b.ComputeHash());
        Assert.Equal(64, a.ComputeHash().Length);
        Assert.True(a.Matches(b.ComputeHash()));
        Assert.StartsWith("{\"payloadVersion\":\"approval-payload-v1\",\"platform\":\"LinkedIn\",\"socialAccountId\"", a.ToCanonicalJson());
        Assert.DoesNotContain("Url\":\"http", a.ToCanonicalJson().Replace("destinationUrl\":\"https://brightline.example/demo", ""));
    }

    [Fact]
    public void Every_public_field_changes_the_hash()
    {
        var (post, v1) = NewPost();
        var baseline = Payload(post, v1, account).ComputeHash();

        string HashOf(PostContent content, SocialAccount destination, MediaAsset? media = null)
        {
            var (p, v) = Post.Create(Guid.NewGuid(), Guid.NewGuid(), Org, SocialPlatform.LinkedIn, content, media is null ? [] : [media], destination, PostVersionSource.Manual, Creator, Now);
            // Same post identity is not part of the payload, so hashes are comparable across posts.
            return Payload(p, v, destination, media).ComputeHash();
        }

        Assert.Equal(baseline, HashOf(Content(), account));
        Assert.NotEqual(baseline, HashOf(Content("Different caption."), account));
        Assert.NotEqual(baseline, HashOf(Content(hashtags: ["Other"]), account));
        Assert.NotEqual(baseline, HashOf(Content(destination: otherAccount), otherAccount));
        Assert.NotEqual(HashOf(Content(media: image), account, image), HashOf(Content(media: image, alt: "Different alt"), account, image));

        var second = MediaAsset.CreateUpload(Guid.NewGuid(), Org, "b.png", "image/png", 100, 1200, 628, new string('d', 64), "k/2.png", Creator, Now);
        string HashOfImages(params MediaAsset[] images)
        {
            var content = Content() with { Images = images.Select(m => new PostImage(m.Id, "Dashboard")).ToList() };
            var (p, v) = Post.Create(Guid.NewGuid(), Guid.NewGuid(), Org, SocialPlatform.LinkedIn, content, images, account, PostVersionSource.Manual, Creator, Now);
            return ApprovalPayload.From(p, v, account, images).ComputeHash();
        }
        Assert.NotEqual(HashOfImages(image), HashOfImages(image, second));
        Assert.NotEqual(HashOfImages(image, second), HashOfImages(second, image));

        var sameIdDifferentBytes = MediaAsset.CreateUpload(image.Id, image.OrganizationId, "a.png", "image/png", 100, 1200, 628, new string('c', 64), image.ObjectKey, Creator, Now);
        Assert.NotEqual(HashOf(Content(media: image), account, image), HashOf(Content(media: sameIdDifferentBytes), account, sameIdDifferentBytes));
    }

    [Fact]
    public void Destination_eligibility_reflects_mode()
    {
        Assert.True(account.IsEligibleDestination);
        Assert.False(SocialAccount.CreateUnconnected(Guid.NewGuid(), Guid.NewGuid(), SocialPlatform.Facebook, "FB", ExecutionMode.NOT_CONFIGURED, Creator, Now).IsEligibleDestination);
        Assert.Throws<BusinessException>(() => SocialAccount.CreateUnconnected(Guid.NewGuid(), Guid.NewGuid(), SocialPlatform.Facebook, "FB", ExecutionMode.LIVE, Creator, Now));
    }

    [Fact]
    public void Destination_must_match_platform_and_workspace()
    {
        var facebook = SocialAccount.CreateUnconnected(Guid.NewGuid(), Org, SocialPlatform.Facebook, "FB", ExecutionMode.SIMULATED, Creator, Now);
        var foreign = SocialAccount.CreateUnconnected(Guid.NewGuid(), Guid.NewGuid(), SocialPlatform.LinkedIn, "LI", ExecutionMode.SIMULATED, Creator, Now);

        Assert.Equal(ErrorCodes.ValidationFailed, Assert.Throws<BusinessException>(() =>
            Post.Create(Guid.NewGuid(), Guid.NewGuid(), Org, SocialPlatform.LinkedIn, Content(destination: facebook), [], facebook, PostVersionSource.Manual, Creator, Now)).Code);
        Assert.Equal(ErrorCodes.TenantMismatch, Assert.Throws<BusinessException>(() =>
            Post.Create(Guid.NewGuid(), Guid.NewGuid(), Org, SocialPlatform.LinkedIn, Content(destination: foreign), [], foreign, PostVersionSource.Manual, Creator, Now)).Code);
    }

    [Fact]
    public void Audit_metadata_refuses_secret_looking_keys() =>
        Assert.Throws<InvalidOperationException>(() => AuditLog.Record(Guid.NewGuid(), Creator, "X", "Post", Guid.NewGuid(),
            new Dictionary<string, string?> { ["accessToken"] = "abc" }, Now));
}
