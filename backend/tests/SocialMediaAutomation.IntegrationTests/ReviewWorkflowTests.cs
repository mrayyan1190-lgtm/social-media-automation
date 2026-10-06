using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SocialMediaAutomation.Application.Audit.DTOs;
using SocialMediaAutomation.Application.Campaigns.DTOs;
using SocialMediaAutomation.Application.Notifications.DTOs;
using SocialMediaAutomation.Application.Organizations.DTOs;
using SocialMediaAutomation.Application.Posts.DTOs;
using SocialMediaAutomation.Application.Reviews.DTOs;
using SocialMediaAutomation.Application.SocialAccounts.DTOs;
using SocialMediaAutomation.Application.Tests;
using SocialMediaAutomation.Domain.Common;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Infrastructure.Persistence.Seed;
using SocialMediaAutomation.IntegrationTests.Infrastructure;

namespace SocialMediaAutomation.IntegrationTests;

/// <summary>Human review and approval through the real API, session cookies and SQL Server.</summary>
[Collection(ApiCollection.Name)]
public class ReviewWorkflowTests(ApiFactory factory)
{
    private static readonly string OrgA = $"/api/v1/organizations/{DemoData.OrganizationAId}";
    private static readonly string OrgB = $"/api/v1/organizations/{DemoData.OrganizationBId}";

    // ---- Fixtures ----------------------------------------------------------------------------------

    private static object Content(Guid? destination, string body = "Close the books in days, not weeks.", Guid? mediaId = null) => new
    {
        Hook = "Month-end, minus the pain",
        Body = body,
        CallToAction = "Book a demo",
        DestinationUrl = "https://brightline.example/demo",
        Hashtags = new[] { "FinanceOps" },
        MediaAssetId = mediaId,
        AltText = mediaId is null ? null : "Dashboard screenshot",
        SocialAccountId = destination,
    };

    private static async Task<SocialAccountDto> AccountAsync(HttpClient client, string platform, ExecutionMode mode = ExecutionMode.SIMULATED, string org = "") =>
        (await (await client.GetAsync($"{(org.Length > 0 ? org : OrgA)}/social-accounts?platform={platform}")).ReadAsync<List<SocialAccountDto>>())
        .First(a => a.ExecutionMode == mode);

    private async Task<(HttpClient Creator, PostDto Post, string CampaignUrl)> DraftAsync(string platform = "LinkedIn", bool withDestination = true, string body = "Close the books in days, not weeks.")
    {
        var creator = await factory.SignInAsync(DemoData.CreatorA);
        var campaign = await creator.CreateCampaignAsync(OrgA);
        var destination = withDestination ? (await AccountAsync(creator, platform)).Id : (Guid?)null;
        var post = await (await creator.CreatePostAsync($"{OrgA}/campaigns/{campaign.Id}",
            new { Platform = platform, Hook = "Month-end, minus the pain", Body = body, Hashtags = new[] { "FinanceOps" }, SocialAccountId = destination })).ReadAsync<PostDto>();
        return (creator, post, $"{OrgA}/campaigns/{campaign.Id}");
    }

    private static Task<HttpResponseMessage> PostWithETagAsync(HttpClient client, string url, object? body, byte[] rowVersion) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body ?? new { }, options: ApiClient.Json),
            Headers = { { "If-Match", ApiClient.ETag(rowVersion) } },
        });

    private static Task<HttpResponseMessage> SubmitAsync(HttpClient client, PostDto post, string? note = null) =>
        PostWithETagAsync(client, $"{OrgA}/posts/{post.Id}/submit-review", new { Note = note }, post.RowVersion);

    private static async Task<ReviewSummaryDto> SubmittedAsync(HttpClient client, PostDto post)
    {
        var response = await SubmitAsync(client, post);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<ReviewSummaryDto>();
    }

    private static Task<HttpResponseMessage> ApproveAsync(HttpClient client, ReviewSummaryDto review) =>
        PostWithETagAsync(client, $"{OrgA}/review-requests/{review.Id}/approve", null, review.RowVersion);

    private static Task<HttpResponseMessage> RequestChangesAsync(HttpClient client, ReviewSummaryDto review, string? explanation) =>
        PostWithETagAsync(client, $"{OrgA}/review-requests/{review.Id}/request-changes", new { Explanation = explanation }, review.RowVersion);

    private static async Task<PostDto> GetPostAsync(HttpClient client, Guid postId) =>
        await (await client.GetAsync($"{OrgA}/posts/{postId}")).ReadAsync<PostDto>();

    private static Task<HttpResponseMessage> ReviseAsync(HttpClient client, PostDto post, object content) =>
        PostWithETagAsync(client, $"{OrgA}/posts/{post.Id}/versions", content, post.RowVersion);

    private async Task<List<AuditLogDto>> AuditAsync(Guid resourceId) =>
        await (await (await factory.SignInAsync(DemoData.AdminA)).GetAsync($"{OrgA}/audit?resourceId={resourceId}")).ReadAsync<List<AuditLogDto>>();

    // ---- Submission ----------------------------------------------------------------------------------

    [Fact]
    public async Task Submission_creates_an_exact_version_review_with_notification_and_audit()
    {
        var (creator, post, _) = await DraftAsync();
        var reviewer = await factory.SignInAsync(DemoData.ReviewerA);

        var review = await SubmittedAsync(creator, post);
        var inReview = await GetPostAsync(creator, post.Id);
        var notifications = await (await reviewer.GetAsync($"{OrgA}/notifications?unreadOnly=true")).ReadAsync<List<NotificationDto>>();
        var audit = await AuditAsync(review.Id);

        Assert.Equal((ReviewStatus.PENDING, post.CurrentVersionId, 1, DemoData.CreatorA.Id), (review.Status, review.PostVersionId, review.VersionNumber, review.SubmittedByUserId));
        Assert.Equal(EditorialState.IN_REVIEW, inReview.EditorialState);
        Assert.Contains(notifications, n => n is { Type: NotificationType.REVIEW_REQUESTED, ResourceType: "ReviewRequest" } && n.ResourceId == review.Id);
        Assert.Contains(audit, a => a.Action == AuditActions.ReviewSubmitted && a.ActorUserId == DemoData.CreatorA.Id && a.Metadata["postVersionId"] == post.CurrentVersionId.ToString());

        // Content under review is frozen: no edits and no second submission.
        await (await ReviseAsync(creator, inReview, Content((await AccountAsync(creator, "LinkedIn")).Id, "Sneaky edit")))
            .AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.InvalidStateTransition);
        await (await SubmitAsync(creator, inReview)).AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.InvalidStateTransition);
    }

    [Fact]
    [Trait("Category", "Acceptance")]
    [Trait("Acceptance", "14 Instagram requires image")]
    public async Task Submission_requires_destination_eligible_mode_media_and_no_critical_blockers()
    {
        var (creator, noDestination, campaignUrl) = await DraftAsync(withDestination: false);
        var notConfigured = await AccountAsync(creator, "Facebook", ExecutionMode.NOT_CONFIGURED);
        var facebook = await (await creator.CreatePostAsync(campaignUrl, new { Platform = "Facebook", Body = "Hello", SocialAccountId = notConfigured.Id })).ReadAsync<PostDto>();
        var instagram = await (await creator.CreatePostAsync(campaignUrl,
            new { Platform = "Instagram", Body = "Visual", SocialAccountId = (await AccountAsync(creator, "Instagram")).Id })).ReadAsync<PostDto>();
        var (_, prohibited, _) = await DraftAsync(body: "Results guaranteed. See https://evil.example/x");

        var missingDestination = await SubmitAsync(creator, noDestination);
        var ineligible = await SubmitAsync(creator, facebook);
        var missingImage = await SubmitAsync(creator, instagram);
        var critical = await SubmitAsync(creator, prohibited);
        var stale = await PostWithETagAsync(creator, $"{OrgA}/posts/{prohibited.Id}/submit-review", null, [0, 0, 0, 0, 0, 0, 0, 1]);

        await missingDestination.AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.SubmissionBlocked);
        Assert.Contains("destination", await missingDestination.Content.ReadAsStringAsync());
        await ineligible.AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.SubmissionBlocked);
        Assert.Contains("NOT_CONFIGURED", await ineligible.Content.ReadAsStringAsync());
        await missingImage.AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.SubmissionBlocked);
        Assert.Contains("require one image", await missingImage.Content.ReadAsStringAsync());
        await critical.AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.SubmissionBlocked);
        Assert.Contains("guaranteed", await critical.Content.ReadAsStringAsync());
        Assert.Contains("URL", await critical.Content.ReadAsStringAsync());
        await stale.AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.ConcurrencyConflict);
        Assert.Equal(EditorialState.DRAFT, (await GetPostAsync(creator, noDestination.Id)).EditorialState);
    }

    [Fact]
    public async Task Instagram_with_jpeg_image_and_simulated_destination_can_be_submitted()
    {
        var creator = await factory.SignInAsync(DemoData.CreatorA);
        var (_, _, campaignUrl) = await DraftAsync();
        var file = new ByteArrayContent(ImageProbeTests.JpegHeader(1080, 1080));
        file.Headers.ContentType = new("image/jpeg");
        var image = await (await creator.PostAsync($"{OrgA}/media", new MultipartFormDataContent { { file, "file", "a.jpg" } }))
            .ReadAsync<Application.Media.DTOs.MediaAssetDto>();
        var instagram = await (await creator.CreatePostAsync(campaignUrl, new
        {
            Platform = "Instagram", Body = "Visual", MediaAssetId = image.Id, AltText = "Blue dashboard", SocialAccountId = (await AccountAsync(creator, "Instagram")).Id,
        })).ReadAsync<PostDto>();

        await SubmittedAsync(creator, instagram);
    }

    [Fact]
    public async Task Destination_from_another_workspace_is_rejected()
    {
        var (creator, post, _) = await DraftAsync();
        var foreignAccount = await AccountAsync(await factory.SignInAsync(DemoData.AdminB), "LinkedIn", org: OrgB);

        await (await ReviseAsync(creator, post, Content(foreignAccount.Id))).AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Reviewer_cannot_submit_and_creator_cannot_decide()
    {
        var (creator, post, _) = await DraftAsync();
        var reviewer = await factory.SignInAsync(DemoData.ReviewerA);

        await (await SubmitAsync(reviewer, post)).AssertErrorAsync(HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        var review = await SubmittedAsync(creator, post);
        await (await ApproveAsync(creator, review)).AssertErrorAsync(HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        await (await RequestChangesAsync(creator, review, "nope")).AssertErrorAsync(HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
    }

    // ---- Distinct reviewer ---------------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Acceptance")]
    [Trait("Acceptance", "03 creator self-approval denied")]
    public async Task Submitter_or_last_editor_cannot_self_approve_when_policy_is_on()
    {
        var admin = await factory.SignInAsync(DemoData.AdminA);
        var (creator, post, _) = await DraftAsync();

        // Admin edits the version (becomes its last editor), Creator submits, Admin tries to approve.
        var adminEdited = await (await ReviseAsync(admin, post, Content((await AccountAsync(admin, "LinkedIn")).Id, "Admin wording"))).ReadAsync<PostDto>();
        var review = await SubmittedAsync(creator, adminEdited);
        await (await ApproveAsync(admin, review)).AssertErrorAsync(HttpStatusCode.Forbidden, ErrorCodes.DistinctReviewerRequired);

        // Admin submits their own draft and tries to approve it.
        var (_, ownDraft, _) = await DraftAsync();
        var adminSubmitted = await SubmittedAsync(admin, await GetPostAsync(admin, ownDraft.Id));
        await (await ApproveAsync(admin, adminSubmitted)).AssertErrorAsync(HttpStatusCode.Forbidden, ErrorCodes.DistinctReviewerRequired);

        var detail = await (await admin.GetAsync($"{OrgA}/review-requests/{review.Id}")).ReadAsync<ReviewDetailDto>();
        Assert.False(detail.CanDecide);
        Assert.Contains("distinct reviewer", detail.CannotDecideReason);
        Assert.Equal(EditorialState.IN_REVIEW, (await GetPostAsync(admin, post.Id)).EditorialState);
    }

    [Fact]
    public async Task Self_approval_is_possible_only_after_an_admin_turns_the_policy_off()
    {
        var admin = await factory.SignInAsync(DemoData.AdminA);
        var (_, draft, _) = await DraftAsync();
        var review = await SubmittedAsync(admin, await GetPostAsync(admin, draft.Id));
        var org = await (await admin.GetAsync(OrgA)).ReadAsync<OrganizationDto>();
        var off = await (await admin.PutJsonAsync($"{OrgA}/settings",
            new { org.Name, org.Industry, org.DefaultLanguage, org.TimeZoneId, RequireDistinctReviewer = false }, ApiClient.ETag(org.RowVersion))).ReadAsync<OrganizationDto>();
        try
        {
            var approved = await (await ApproveAsync(admin, review)).ReadAsync<ReviewSummaryDto>();
            Assert.Equal(ReviewStatus.APPROVED, approved.Status);
            Assert.Contains(await AuditAsync(review.Id), a => a.Action == AuditActions.ContentApproved && a.Metadata["requireDistinctReviewer"] == "False");
        }
        finally
        {
            await admin.PutJsonAsync($"{OrgA}/settings", new { org.Name, org.Industry, org.DefaultLanguage, org.TimeZoneId, RequireDistinctReviewer = true }, ApiClient.ETag(off.RowVersion));
        }
    }

    [Fact]
    [Trait("Category", "Acceptance")]
    [Trait("Acceptance", "02 cross-tenant mutation denied")]
    public async Task Other_workspace_cannot_see_or_decide_the_review()
    {
        var (creator, post, _) = await DraftAsync();
        var review = await SubmittedAsync(creator, post);
        var adminB = await factory.SignInAsync(DemoData.AdminB);

        await (await adminB.GetAsync($"{OrgA}/review-requests/{review.Id}")).AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
        await (await PostWithETagAsync(adminB, $"{OrgA}/review-requests/{review.Id}/approve", null, review.RowVersion))
            .AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
        await (await PostWithETagAsync(adminB, $"{OrgB}/review-requests/{review.Id}/approve", null, review.RowVersion))
            .AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
        Assert.DoesNotContain(await (await adminB.GetAsync($"{OrgB}/review-requests")).ReadAsync<List<ReviewSummaryDto>>(), r => r.Id == review.Id);
    }

    // ---- Approval ------------------------------------------------------------------------------------

    [Fact]
    public async Task Approval_binds_the_exact_version_and_its_payload_hash()
    {
        var (creator, post, _) = await DraftAsync();
        var review = await SubmittedAsync(creator, post);
        var reviewer = await factory.SignInAsync(DemoData.ReviewerA);
        var detail = await (await reviewer.GetAsync($"{OrgA}/review-requests/{review.Id}")).ReadAsync<ReviewDetailDto>();

        var approved = await (await ApproveAsync(reviewer, detail.Review)).ReadAsync<ReviewSummaryDto>();
        var after = await GetPostAsync(creator, post.Id);

        Assert.True(detail.CanDecide);
        Assert.Equal((ReviewStatus.APPROVED, DemoData.ReviewerA.Id), (approved.Status, approved.DecidedByUserId));
        Assert.Equal((EditorialState.APPROVED, (Guid?)review.Id, review.PostVersionId), (after.EditorialState, after.ActiveApprovalId, after.CurrentVersionId));

        await using var db = factory.CreateDbContext();
        var row = await db.ReviewRequests.AsNoTracking().SingleAsync(r => r.Id == review.Id);
        var version = await db.PostVersions.AsNoTracking().SingleAsync(v => v.Id == review.PostVersionId);
        var postRow = await db.Posts.AsNoTracking().SingleAsync(p => p.Id == post.Id);
        var account = await db.SocialAccounts.AsNoTracking().SingleAsync(a => a.Id == version.SocialAccountId);
        var recomputed = ApprovalPayload.From(postRow, version, account, null);
        Assert.Equal(recomputed.ComputeHash(), row.ApprovedPayloadHash);
        Assert.Equal(recomputed.ToCanonicalJson(), row.ApprovedPayloadJson);
        Assert.Equal(ApprovalPayload.CurrentVersion, row.ApprovedPayloadVersion);

        Assert.Contains(await (await creator.GetAsync($"{OrgA}/notifications")).ReadAsync<List<NotificationDto>>(),
            n => n.Type == NotificationType.APPROVED && n.ResourceId == review.Id);
        Assert.Contains(await AuditAsync(review.Id), a => a.Action == AuditActions.ContentApproved && a.Metadata["payloadHash"] == row.ApprovedPayloadHash);

        // The database refuses an active approval that points at any version other than the current one.
        var otherReview = await db.ReviewRequests.AsNoTracking().FirstAsync(r => r.PostId != post.Id && r.OrganizationId == DemoData.OrganizationAId);
        var error = await Assert.ThrowsAsync<SqlException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Posts SET ActiveApprovalId = {otherReview.Id} WHERE Id = {post.Id}"));
        Assert.Equal(547, error.Number);
    }

    [Fact]
    [Trait("Category", "Acceptance")]
    [Trait("Acceptance", "06 stale review rejected")]
    public async Task Stale_or_repeated_decisions_are_rejected()
    {
        var (creator, post, _) = await DraftAsync();
        var review = await SubmittedAsync(creator, post);
        var reviewer = await factory.SignInAsync(DemoData.ReviewerA);

        var wrongETag = await PostWithETagAsync(reviewer, $"{OrgA}/review-requests/{review.Id}/approve", null, [0, 0, 0, 0, 0, 0, 0, 9]);
        var missingETag = await reviewer.PostAsync($"{OrgA}/review-requests/{review.Id}/approve", null);
        var changes = await (await RequestChangesAsync(reviewer, review, "Please soften the claim.")).ReadAsync<ReviewSummaryDto>();
        var approveAfterDecision = await ApproveAsync(reviewer, changes);
        var approveWithOldETag = await ApproveAsync(reviewer, review);

        await wrongETag.AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.ConcurrencyConflict);
        await missingETag.AssertErrorAsync(HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        await approveAfterDecision.AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.InvalidStateTransition);
        await approveWithOldETag.AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.ConcurrencyConflict);
        Assert.Equal((EditorialState.CHANGES_REQUESTED, (Guid?)null), ((await GetPostAsync(creator, post.Id)).EditorialState, (await GetPostAsync(creator, post.Id)).ActiveApprovalId));
    }

    // ---- Request changes -----------------------------------------------------------------------------

    [Fact]
    public async Task Request_changes_requires_explanation_then_creator_revises_and_resubmits()
    {
        var (creator, post, _) = await DraftAsync();
        var review = await SubmittedAsync(creator, post);
        var reviewer = await factory.SignInAsync(DemoData.ReviewerA);

        await (await RequestChangesAsync(reviewer, review, null)).AssertErrorAsync(HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        await (await RequestChangesAsync(reviewer, review, "   ")).AssertErrorAsync(HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        Assert.Equal(EditorialState.IN_REVIEW, (await GetPostAsync(creator, post.Id)).EditorialState);

        var decided = await (await RequestChangesAsync(reviewer, review, "Mention the 40 teams fact.")).ReadAsync<ReviewSummaryDto>();
        var changesRequested = await GetPostAsync(creator, post.Id);
        await (await SubmitAsync(creator, changesRequested)).AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.InvalidStateTransition);

        var v2 = await (await ReviseAsync(creator, changesRequested, Content(changesRequested.CurrentVersion.SocialAccountId, "40 finance teams already close faster.")))
            .ReadAsync<PostDto>();
        var second = await SubmittedAsync(creator, v2);
        var history = await (await reviewer.GetAsync($"{OrgA}/review-requests?postId={post.Id}")).ReadAsync<List<ReviewSummaryDto>>();

        Assert.Equal((ReviewStatus.CHANGES_REQUESTED, "Mention the 40 teams fact."), (decided.Status, decided.DecisionComment));
        Assert.Equal(EditorialState.CHANGES_REQUESTED, changesRequested.EditorialState);
        Assert.Equal((EditorialState.DRAFT, 2), (v2.EditorialState, v2.CurrentVersion.VersionNumber));
        Assert.Equal(2, second.VersionNumber);
        Assert.Equal([(2, ReviewStatus.PENDING), (1, ReviewStatus.CHANGES_REQUESTED)], history.Select(r => (r.VersionNumber, r.Status)));
        Assert.Contains(await (await creator.GetAsync($"{OrgA}/notifications")).ReadAsync<List<NotificationDto>>(),
            n => n.Type == NotificationType.CHANGES_REQUESTED && n.Message == "Mention the 40 teams fact.");
    }

    // ---- Edit after approval -------------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Acceptance")]
    [Trait("Acceptance", "07 edit invalidates approval")]
    public async Task Editing_approved_content_creates_unapproved_draft_and_keeps_approved_version()
    {
        var (creator, post, _) = await DraftAsync();
        var review = await SubmittedAsync(creator, post);
        await (await ApproveAsync(await factory.SignInAsync(DemoData.ReviewerA), review)).ReadAsync<ReviewSummaryDto>();
        var approved = await GetPostAsync(creator, post.Id);

        var edited = await (await ReviseAsync(creator, approved, Content(approved.CurrentVersion.SocialAccountId, "Edited after approval."))).ReadAsync<PostDto>();
        var versions = await (await creator.GetAsync($"{OrgA}/posts/{post.Id}/versions")).ReadAsync<List<PostVersionDto>>();
        var oldReview = (await (await creator.GetAsync($"{OrgA}/review-requests?postId={post.Id}")).ReadAsync<List<ReviewSummaryDto>>()).Single();

        Assert.Equal((EditorialState.DRAFT, (Guid?)null, 2), (edited.EditorialState, edited.ActiveApprovalId, edited.CurrentVersion.VersionNumber));
        Assert.Equal((approved.CurrentVersionId, approved.CurrentVersion.Body), (versions[1].Id, versions[1].Body));
        Assert.Equal((ReviewStatus.APPROVED, approved.CurrentVersionId), (oldReview.Status, oldReview.PostVersionId)); // historical approval preserved
        Assert.Contains(await AuditAsync(post.Id), a => a.Action == AuditActions.ApprovalRevoked && a.Metadata["revokedReviewRequestId"] == review.Id.ToString());
        Assert.Equal(EditorialState.IN_REVIEW, (await GetPostAsync(creator, (await SubmittedAsync(creator, edited)).PostId)).EditorialState); // needs a new review
    }

    // ---- Atomicity -----------------------------------------------------------------------------------

    [Fact]
    public async Task Approval_is_atomic_when_part_of_the_transaction_fails()
    {
        var (creator, post, _) = await DraftAsync();
        var review = await SubmittedAsync(creator, post);
        var reviewer = await factory.SignInAsync(DemoData.ReviewerA);
        await using var db = factory.CreateDbContext();
        // Make the notification insert (part of the approval transaction) fail inside SQL Server.
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER TR_Test_FailApprovalNotifications ON Notifications AFTER INSERT AS " +
            "IF EXISTS (SELECT 1 FROM inserted WHERE Type = 'APPROVED') THROW 51001, 'Simulated failure', 1;");
        try
        {
            var response = await ApproveAsync(reviewer, review);
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS TR_Test_FailApprovalNotifications;");
        }

        var reviewRow = await db.ReviewRequests.AsNoTracking().SingleAsync(r => r.Id == review.Id);
        var postRow = await db.Posts.AsNoTracking().SingleAsync(p => p.Id == post.Id);
        Assert.Equal((ReviewStatus.PENDING, null), (reviewRow.Status, reviewRow.ApprovedPayloadHash));
        Assert.Equal((EditorialState.IN_REVIEW, (Guid?)null), (postRow.EditorialState, postRow.ActiveApprovalId));
        Assert.DoesNotContain(await db.AuditLogs.AsNoTracking().Where(a => a.ResourceId == review.Id).Select(a => a.Action).ToListAsync(), a => a == AuditActions.ContentApproved);

        // Nothing was half-done, so the same approval succeeds once the fault is gone.
        Assert.Equal(ReviewStatus.APPROVED, (await (await ApproveAsync(reviewer, review)).ReadAsync<ReviewSummaryDto>()).Status);
    }

    // ---- Review screen -------------------------------------------------------------------------------

    [Fact]
    public async Task Review_screen_shows_exact_content_campaign_destination_and_comments()
    {
        var (creator, post, _) = await DraftAsync();
        var review = await SubmittedAsync(creator, post);
        var reviewer = await factory.SignInAsync(DemoData.ReviewerA);
        await reviewer.PostAsJsonAsync($"{OrgA}/review-requests/{review.Id}/comments", new { Body = "Can we cite the source?" });
        await creator.PostAsJsonAsync($"{OrgA}/review-requests/{review.Id}/comments", new { Body = "Added in the notes." });
        await (await reviewer.PostAsJsonAsync($"{OrgA}/review-requests/{review.Id}/comments", new { Body = " " }))
            .AssertErrorAsync(HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);

        var detail = await (await reviewer.GetAsync($"{OrgA}/review-requests/{review.Id}")).ReadAsync<ReviewDetailDto>();

        Assert.Equal((post.CurrentVersionId, 1, "Close the books in days, not weeks."), (detail.Version.Id, detail.Version.VersionNumber, detail.Version.Body));
        Assert.NotNull(detail.Review.CampaignName);
        Assert.Equal((ExecutionMode.SIMULATED, SocialPlatform.LinkedIn), (detail.Destination!.ExecutionMode, detail.Destination.Platform));
        Assert.Equal(["Can we cite the source?", "Added in the notes."], detail.Comments.Select(c => c.Body));
        Assert.True(detail.IsCurrentVersion);
        Assert.Single(detail.History);
    }

    [Fact]
    public async Task Notifications_are_private_to_their_recipient()
    {
        var (creator, post, _) = await DraftAsync();
        await SubmittedAsync(creator, post);
        var reviewer = await factory.SignInAsync(DemoData.ReviewerA);
        var mine = await (await reviewer.GetAsync($"{OrgA}/notifications?unreadOnly=true")).ReadAsync<List<NotificationDto>>();
        var target = mine.First();

        await (await creator.PostAsync($"{OrgA}/notifications/{target.Id}/read", null)).AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
        Assert.Equal(HttpStatusCode.NoContent, (await reviewer.PostAsync($"{OrgA}/notifications/{target.Id}/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await reviewer.PostAsync($"{OrgA}/notifications/read-all", null)).StatusCode);
        Assert.Empty(await (await reviewer.GetAsync($"{OrgA}/notifications?unreadOnly=true")).ReadAsync<List<NotificationDto>>());
        await (await creator.GetAsync($"{OrgA}/audit")).AssertErrorAsync(HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
    }
}
