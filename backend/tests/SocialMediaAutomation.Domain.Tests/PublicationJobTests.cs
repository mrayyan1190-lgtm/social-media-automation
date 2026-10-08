using SocialMediaAutomation.Domain.Common;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Domain.Tests;

public class PublicationJobTests
{
    private static readonly Guid Creator = Guid.NewGuid();
    private static readonly Guid Reviewer = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static readonly Guid Org = Guid.NewGuid();

    private static string Code(Action act) => Assert.Throws<BusinessException>(act).Code;

    private SocialAccount Account(ExecutionMode mode = ExecutionMode.SIMULATED) =>
        SocialAccount.CreateUnconnected(Guid.NewGuid(), Org, SocialPlatform.LinkedIn, "LI", mode, Creator, Now);

    private (Post Post, PostVersion Version, ReviewRequest Approval, SocialAccount Account) Approved(SocialAccount? destination = null)
    {
        var account = destination ?? Account();
        var (post, v1) = Post.Create(Guid.NewGuid(), Guid.NewGuid(), Org, SocialPlatform.LinkedIn,
            new PostContent("Hook", "Close the books faster.", null, null, [], null, account.Id), [], account, PostVersionSource.Manual, Creator, Now);
        var review = ReviewRequest.Submit(Guid.NewGuid(), post, v1, Creator, null, null, Now);
        post.SubmitForReview(review, Now);
        review.Approve(Reviewer, v1, true, ApprovalPayload.From(post, v1, account, []), Now);
        post.Approve(review, Now);
        return (post, v1, review, account);
    }

    private static PublicationJob Job((Post Post, PostVersion Version, ReviewRequest Approval, SocialAccount Account) a, PublishTime? time = null) =>
        PublicationJob.Create(Guid.NewGuid(), a.Post, a.Version, a.Approval, a.Account, ApprovalPayload.From(a.Post, a.Version, a.Account, []),
            time ?? PublishTime.Now(Now), Creator, Now);

    // ---- Time resolution ------------------------------------------------------------------------------

    [Fact]
    public void Local_time_resolves_to_utc_and_keeps_the_original_input()
    {
        var time = PublishTime.Resolve(new DateOnly(2026, 7, 1), new TimeOnly(9, 30), "America/New_York", null, Now);
        Assert.Equal(new DateTime(2026, 7, 1, 13, 30, 0, DateTimeKind.Utc), time.Utc);
        Assert.Equal((new DateTime(2026, 7, 1, 9, 30, 0), "America/New_York", -240), (time.Local, time.TimeZoneId, time.UtcOffsetMinutes));
    }

    [Theory]
    [InlineData("America/New_York", 2026, 3, 8, 2, 30)] // spring forward: 02:00 -> 03:00
    [InlineData("Europe/London", 2026, 3, 29, 1, 30)]   // spring forward: 01:00 -> 02:00
    public void Nonexistent_dst_time_is_rejected(string zone, int y, int m, int d, int h, int min) =>
        Assert.Equal(ErrorCodes.LocalTimeNonexistent, Code(() => PublishTime.Resolve(new DateOnly(y, m, d), new TimeOnly(h, min), zone, null, Now)));

    [Fact]
    public void Ambiguous_dst_time_requires_an_explicit_offset_choice()
    {
        var date = new DateOnly(2026, 11, 1); // New York falls back at 02:00, so 01:30 happens twice
        var at = new TimeOnly(1, 30);
        Assert.Equal(ErrorCodes.LocalTimeAmbiguous, Code(() => PublishTime.Resolve(date, at, "America/New_York", null, Now)));
        Assert.Equal(ErrorCodes.LocalTimeAmbiguous, Code(() => PublishTime.Resolve(date, at, "America/New_York", -60, Now)));

        var first = PublishTime.Resolve(date, at, "America/New_York", -240, Now);
        var second = PublishTime.Resolve(date, at, "America/New_York", -300, Now);
        Assert.Equal(new DateTime(2026, 11, 1, 5, 30, 0, DateTimeKind.Utc), first.Utc);
        Assert.Equal(new DateTime(2026, 11, 1, 6, 30, 0, DateTimeKind.Utc), second.Utc);
        Assert.Equal((-240, -300), (first.UtcOffsetMinutes, second.UtcOffsetMinutes));
    }

    [Theory]
    [InlineData("Eastern Standard Time")] // Windows id, not IANA
    [InlineData("Mars/Olympus")]
    [InlineData("")]
    public void Time_zone_must_be_iana(string zone) =>
        Assert.Equal(ErrorCodes.ValidationFailed, Code(() => PublishTime.Resolve(new DateOnly(2026, 7, 1), new TimeOnly(9, 0), zone, null, Now)));

    [Fact]
    public void Past_or_far_future_time_is_rejected()
    {
        Assert.Equal(ErrorCodes.ValidationFailed, Code(() => PublishTime.Resolve(new DateOnly(2025, 12, 31), new TimeOnly(9, 0), "UTC", null, Now)));
        Assert.Equal(ErrorCodes.ValidationFailed, Code(() => PublishTime.Resolve(new DateOnly(2028, 1, 1), new TimeOnly(9, 0), "UTC", null, Now)));
    }

    // ---- Creation guards ------------------------------------------------------------------------------

    [Fact]
    public void Job_freezes_the_approved_payload_and_snapshots_the_mode()
    {
        var approved = Approved();
        var time = PublishTime.Resolve(new DateOnly(2026, 2, 1), new TimeOnly(9, 0), "Europe/London", null, Now);
        var job = Job(approved, time);

        Assert.Equal((DeliveryState.SCHEDULED, ExecutionMode.SIMULATED, approved.Version.Id, approved.Approval.Id),
            (job.DeliveryState, job.ExecutionMode, job.PostVersionId, job.ReviewRequestId));
        Assert.Equal(approved.Approval.ApprovedPayloadHash, job.PayloadHash);
        Assert.Equal(approved.Approval.ApprovedPayloadJson, job.PayloadJson);
        Assert.Equal((time.Utc, time.Utc), (job.PublishAtUtc, job.NextActionAtUtc));
        Assert.Equal((0, PublicationJob.DefaultMaxAttempts), (job.AttemptCount, job.MaxAttempts));
    }

    [Fact]
    public void Unapproved_post_cannot_create_a_job()
    {
        var account = Account();
        var (post, v1) = Post.Create(Guid.NewGuid(), Guid.NewGuid(), Org, SocialPlatform.LinkedIn,
            new PostContent("Hook", "Body", null, null, [], null, account.Id), [], account, PostVersionSource.Manual, Creator, Now);
        var review = ReviewRequest.Submit(Guid.NewGuid(), post, v1, Creator, null, null, Now);
        post.SubmitForReview(review, Now);

        Assert.Equal(ErrorCodes.ApprovalRequired, Code(() => PublicationJob.Create(Guid.NewGuid(), post, v1, review, account,
            ApprovalPayload.From(post, v1, account, []), PublishTime.Now(Now), Creator, Now)));
    }

    [Fact]
    public void Stale_approval_or_changed_payload_cannot_create_a_job()
    {
        var approved = Approved();
        var v2 = approved.Post.Revise(Guid.NewGuid(), approved.Version,
            new PostContent("Hook", "Edited.", null, null, [], null, approved.Account.Id), [], approved.Account, PostVersionSource.Manual, Creator, Now)!;
        Assert.Equal(ErrorCodes.ApprovalRequired, Code(() => Job(approved)));

        // A payload that does not hash to what the reviewer approved (e.g. tampered stored data) is refused.
        var other = Approved();
        var tampered = ApprovalPayload.From(other.Post, other.Version, other.Account, []) with { Body = "Something else" };
        Assert.Equal(ErrorCodes.ApprovalStale, Code(() => PublicationJob.Create(Guid.NewGuid(), other.Post, other.Version, other.Approval, other.Account,
            tampered, PublishTime.Now(Now), Creator, Now)));
        Assert.NotNull(v2);
    }

    [Fact]
    public void Not_configured_or_disconnected_destination_cannot_create_a_job()
    {
        var approved = Approved();
        var notConfigured = Account(ExecutionMode.NOT_CONFIGURED);
        Assert.Throws<InvalidOperationException>(() => ApprovalPayload.From(approved.Post, approved.Version, notConfigured, []));
        Assert.Equal(ErrorCodes.ApprovalStale, Code(() => PublicationJob.Create(Guid.NewGuid(), approved.Post, approved.Version, approved.Approval,
            notConfigured, ApprovalPayload.From(approved.Post, approved.Version, approved.Account, []), PublishTime.Now(Now), Creator, Now)));

        var connection = OAuthConnection.Create(Guid.NewGuid(), Org, SocialPlatform.LinkedIn, "m", "Ada", Creator, Now);
        var live = SocialAccount.CreateConnected(Guid.NewGuid(), connection, "urn:li:person:m", "Ada", ExecutionMode.LIVE, ["publish_text"], Creator, Now);
        var liveApproved = Approved(live);
        live.MarkDisconnected(Now);
        Assert.Equal(ErrorCodes.SocialAccountNotReady, Code(() => Job(liveApproved)));
    }

    // ---- Cancel / reschedule --------------------------------------------------------------------------

    [Fact]
    public void Scheduled_job_can_be_canceled_and_reactivated_but_not_twice_canceled()
    {
        var job = Job(Approved(), PublishTime.Resolve(new DateOnly(2026, 2, 1), new TimeOnly(9, 0), "UTC", null, Now));
        job.Cancel(Creator, Now);
        Assert.Equal((DeliveryState.CANCELED, (DateTime?)null, Creator), (job.DeliveryState, job.NextActionAtUtc, job.CanceledByUserId!.Value));
        Assert.Equal(ErrorCodes.InvalidStateTransition, Code(() => job.Cancel(Creator, Now)));

        var later = PublishTime.Resolve(new DateOnly(2026, 3, 1), new TimeOnly(9, 0), "UTC", null, Now);
        job.Reschedule(later, Now);
        Assert.Equal((DeliveryState.SCHEDULED, later.Utc, (Guid?)null), (job.DeliveryState, job.NextActionAtUtc!.Value, job.CanceledByUserId));
    }
}
