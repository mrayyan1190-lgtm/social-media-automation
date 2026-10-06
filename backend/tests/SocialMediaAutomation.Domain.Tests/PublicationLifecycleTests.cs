using System.Reflection;
using SocialMediaAutomation.Domain.Common;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Domain.Tests;

/// <summary>Worker-side transitions of a publication job. Claiming happens in SQL; tests set the claimed state directly.</summary>
public class PublicationLifecycleTests
{
    private static readonly Guid Creator = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private readonly Campaign campaign = CampaignTests.New();

    private PublicationJob Job(ExecutionMode mode)
    {
        SocialAccount account;
        if (mode == ExecutionMode.SIMULATED)
        {
            account = SocialAccount.CreateUnconnected(Guid.NewGuid(), campaign.OrganizationId, SocialPlatform.LinkedIn, "LI", mode, Creator, Now);
        }
        else
        {
            var connection = OAuthConnection.Create(Guid.NewGuid(), campaign.OrganizationId, SocialPlatform.LinkedIn, "m", "Ada", Creator, Now);
            account = SocialAccount.CreateConnected(Guid.NewGuid(), connection, "urn:li:person:m", "Ada", mode, ["publish_text"], Creator, Now);
        }
        var (post, v1) = Post.Create(Guid.NewGuid(), Guid.NewGuid(), campaign.OrganizationId, campaign, SocialPlatform.LinkedIn,
            new PostContent("Hook", "Body", null, null, [], null, null, account.Id), null, account, PostVersionSource.Manual, Creator, Now);
        var review = ReviewRequest.Submit(Guid.NewGuid(), post, v1, Creator, null, null, Now);
        post.SubmitForReview(review, Now);
        review.Approve(Guid.NewGuid(), v1, true, ApprovalPayload.From(post, v1, account, null), Now);
        post.Approve(review, Now);
        return PublicationJob.Create(Guid.NewGuid(), post, v1, review, account, ApprovalPayload.From(post, v1, account, null), PublishTime.Now(Now), Creator, Now);
    }

    /// <summary>What the SQL claim does: PROCESSING, a fresh lease token, one more attempt.</summary>
    private static Guid Claim(PublicationJob job, DateTime? leaseExpires = null)
    {
        var token = Guid.NewGuid();
        Set(job, nameof(PublicationJob.DeliveryState), DeliveryState.PROCESSING);
        Set(job, nameof(PublicationJob.LeaseToken), (Guid?)token);
        Set(job, nameof(PublicationJob.LeaseExpiresAtUtc), (DateTime?)(leaseExpires ?? Now.AddMinutes(5)));
        Set(job, nameof(PublicationJob.AttemptCount), job.AttemptCount + 1);
        return token;
    }

    private static void Set(PublicationJob job, string property, object? value) =>
        typeof(PublicationJob).GetProperty(property, BindingFlags.Public | BindingFlags.Instance)!.SetValue(job, value);

    [Fact]
    public void Every_transition_is_fenced_by_the_lease_token()
    {
        var job = Job(ExecutionMode.LIVE);
        var token = Claim(job);
        Assert.Equal(ErrorCodes.ConcurrencyConflict, Assert.Throws<BusinessException>(() => job.MarkDispatchStarted(Guid.NewGuid(), Now)).Code);
        job.MarkDispatchStarted(token, Now);
        job.CompletePublished(token, "urn:li:share:1", null, Now);
        Assert.Throws<BusinessException>(() => job.CompletePublished(token, "urn:li:share:2", null, Now)); // lease released
        Assert.Equal(("urn:li:share:1", (Guid?)null), (job.ExternalPostId, job.LeaseToken));
    }

    [Fact]
    public void Simulated_and_live_outcomes_cannot_be_swapped()
    {
        var simulated = Job(ExecutionMode.SIMULATED);
        var simToken = Claim(simulated);
        Assert.Throws<InvalidOperationException>(() => simulated.CompletePublished(simToken, null, null, Now));
        simulated.CompleteSimulated(simToken, Now);
        Assert.Equal((DeliveryState.SIMULATED, (string?)null), (simulated.DeliveryState, simulated.ExternalPostId));

        var live = Job(ExecutionMode.LIVE);
        Assert.Throws<InvalidOperationException>(() => live.CompleteSimulated(Claim(live), Now));
    }

    [Fact]
    public void Safe_retry_backs_off_clears_the_dispatch_checkpoint_and_is_bounded()
    {
        var job = Job(ExecutionMode.LIVE);
        for (var attempt = 1; attempt <= job.MaxAttempts; attempt++)
        {
            var token = Claim(job);
            job.MarkDispatchStarted(token, Now);
            job.ScheduleRetry(token, "THROTTLED", "rate limited", Now);
            if (attempt < job.MaxAttempts)
                Assert.Equal((DeliveryState.RETRY_PENDING, (DateTime?)null), (job.DeliveryState, job.DispatchStartedAtUtc));
        }
        Assert.Equal(DeliveryState.FAILED, job.DeliveryState);
        Assert.Contains("gave up after 3 attempts", job.LastErrorMessage);
    }

    [Fact]
    public void Abandoned_lease_after_a_real_dispatch_is_reconciled_but_before_dispatch_it_is_not()
    {
        var beforeDispatch = Job(ExecutionMode.LIVE);
        Claim(beforeDispatch, leaseExpires: Now.AddMinutes(-1));
        Assert.False(beforeDispatch.ReconcileAbandoned(Now)); // reclaimed by the claim query instead

        var afterDispatch = Job(ExecutionMode.LIVE);
        var token = Claim(afterDispatch);
        afterDispatch.MarkDispatchStarted(token, Now);
        Set(afterDispatch, nameof(PublicationJob.LeaseExpiresAtUtc), (DateTime?)Now.AddMinutes(-1));
        Assert.True(afterDispatch.ReconcileAbandoned(Now));
        Assert.Equal((DeliveryState.RECONCILIATION_REQUIRED, PublicationErrorCodes.LeaseLostAfterDispatch), (afterDispatch.DeliveryState, afterDispatch.LastErrorCode));

        var simulated = Job(ExecutionMode.SIMULATED);
        var simToken = Claim(simulated);
        simulated.MarkDispatchStarted(simToken, Now);
        Set(simulated, nameof(PublicationJob.LeaseExpiresAtUtc), (DateTime?)Now.AddMinutes(-1));
        Assert.False(simulated.ReconcileAbandoned(Now)); // nothing external to reconcile
    }

    [Fact]
    public void Lateness_applies_only_before_the_first_dispatch()
    {
        var job = Job(ExecutionMode.SIMULATED);
        Assert.False(job.IsLate(Now.Add(PublicationJob.MaxLateness)));
        Assert.True(job.IsLate(Now.Add(PublicationJob.MaxLateness).AddMinutes(1)));
        var token = Claim(job);
        job.RecordMediaContainer(token, "c-1", Now);
        Assert.False(job.IsLate(Now.AddDays(1)));
    }
}
