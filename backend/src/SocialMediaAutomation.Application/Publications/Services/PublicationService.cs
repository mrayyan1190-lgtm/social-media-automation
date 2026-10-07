using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.Audit.Services;
using SocialMediaAutomation.Application.Common;
using SocialMediaAutomation.Application.Common.Authorization;
using SocialMediaAutomation.Application.Publications.DTOs;
using SocialMediaAutomation.Domain.Common;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Application.Publications.Services;

/// <summary>
/// Publication intent (PRD §25-27, ARCHITECTURE.md §25-28). Schedule and Publish Now both only persist a durable
/// PublicationJob; no provider is ever called here. Delivery is the publishing worker's job.
/// </summary>
public sealed class PublicationService(
    OrganizationAuthorizer authorizer,
    ICorrelationContext correlation,
    IPostRepository posts,
    IReviewRepository reviews,
    ISocialAccountRepository socialAccounts,
    IMediaRepository media,
    IPublicationRepository publications,
    ActivityRecorder activity,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public const string JobResource = "PublicationJob";

    /// <summary>Schedules the exact approved current version. <paramref name="schedule"/> null means Publish Now.</summary>
    public async Task<ScheduleResult> ScheduleAsync(
        Guid organizationId, Guid postId, Guid postVersionId, LocalSchedule? schedule, byte[]? expectedPostRowVersion, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.SchedulePublication, cancellationToken);
        var post = await LoadPostAsync(access, postId, cancellationToken);
        ConcurrencyGuard.EnsureCurrent(post.RowVersion, expectedPostRowVersion);
        var time = Resolve(schedule);
        if (postVersionId != post.CurrentVersionId)
            throw new BusinessException(ErrorCodes.ApprovalStale, "The version you are scheduling is no longer the post's current version. Reload the post.");
        var (version, approval, account, payload) = await LoadApprovedAsync(access, post, cancellationToken);

        var existing = await publications.FindIntentAsync(access.OrganizationId, version.Id, account.Id, cancellationToken);
        if (existing is not null)
            return new(await ReuseAsync(access, existing, time, schedule is null, cancellationToken), Created: false);

        var job = PublicationJob.Create(Guid.NewGuid(), post, version, approval, account, payload, time, access.UserId, Now);
        job.AttachCorrelation(correlation.CorrelationId ?? CorrelationIds.New());
        publications.Add(job);
        AuditSchedule(access, AuditActions.PublicationScheduled, job, publishNow: schedule is null);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (BusinessException ex) when (ex.Code == ErrorCodes.ConcurrencyConflict)
        {
            // A concurrent click may have won the unique (Organization, PostVersion, SocialAccount) intent; answer with that one.
            unitOfWork.DiscardChanges();
            var winner = await publications.FindIntentAsync(access.OrganizationId, version.Id, account.Id, cancellationToken);
            if (winner is null)
                throw;
            if (!winner.IsSameSchedule(time) && schedule is not null)
                throw Conflict(winner);
            return new(await ToDtoAsync(access, winner, cancellationToken), Created: false);
        }
        return new(await ToDtoAsync(access, job, cancellationToken), Created: true);
    }

    /// <summary>Moves a not-yet-dispatched intent (or reactivates a canceled one) after re-verifying its approval.</summary>
    public async Task<PublicationJobDto> RescheduleAsync(
        Guid organizationId, Guid jobId, LocalSchedule? schedule, byte[]? expectedRowVersion, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.SchedulePublication, cancellationToken);
        var job = await LoadJobAsync(access, jobId, cancellationToken);
        ConcurrencyGuard.EnsureCurrent(job.RowVersion, expectedRowVersion);
        await MoveAsync(access, job, Resolve(schedule), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(access, job, cancellationToken);
    }

    public async Task<PublicationJobDto> CancelAsync(Guid organizationId, Guid jobId, byte[]? expectedRowVersion, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.SchedulePublication, cancellationToken);
        var job = await LoadJobAsync(access, jobId, cancellationToken);
        ConcurrencyGuard.EnsureCurrent(job.RowVersion, expectedRowVersion);
        job.Cancel(access.UserId, Now);
        activity.Audit(access.OrganizationId, access.UserId, AuditActions.PublicationCanceled, JobResource, job.Id,
            new Dictionary<string, string?> { ["postId"] = job.PostId.ToString(), ["postVersionId"] = job.PostVersionId.ToString(), ["reason"] = "user" });
        // The worker claims jobs with a conditional update that bumps the row version, so a claim racing this cancel
        // makes the save fail with CONCURRENCY_CONFLICT instead of canceling an in-flight job.
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(access, job, cancellationToken);
    }

    public async Task<IReadOnlyList<PublicationJobDto>> ListAsync(Guid organizationId, PublicationQuery query, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ViewCampaigns, cancellationToken);
        if (query.FromUtc > query.ToUtc)
            throw Guard.Invalid("Date range", "must start before it ends");
        if (query.FromUtc is { } from && query.ToUtc is { } to && to - from > TimeSpan.FromDays(400))
            throw Guard.Invalid("Date range", "must be at most 400 days");
        return await publications.ListAsync(access.OrganizationId, query with { Take = Math.Clamp(query.Take, 1, 500) }, cancellationToken);
    }

    /// <summary>Every worker pass over the job, oldest first: outcome, normalized error and provider evidence.</summary>
    public async Task<IReadOnlyList<PublicationAttemptDto>> ListAttemptsAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ViewCampaigns, cancellationToken);
        var job = await LoadJobAsync(access, jobId, cancellationToken);
        return await publications.ListAttemptsAsync(access.OrganizationId, job.Id, cancellationToken);
    }

    public async Task<PublicationJobDto> GetAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ViewCampaigns, cancellationToken);
        return await ToDtoAsync(access, await LoadJobAsync(access, jobId, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Called before a post's current (approved) version is replaced (PRD §21, ARCHITECTURE.md §37.4). Rejects the edit
    /// when dispatch may already have started, and otherwise returns the safe jobs that must be canceled together with the
    /// new version. Nothing is modified here, so an edit that ends up changing nothing cancels nothing.
    /// </summary>
    public async Task<IReadOnlyList<PublicationJob>> JobsToCancelForEditAsync(Post post, CancellationToken cancellationToken)
    {
        if (post.CurrentVersionId is not { } versionId)
            return [];
        var jobs = await publications.ListForVersionAsync(post.OrganizationId, versionId, cancellationToken);
        if (jobs.FirstOrDefault(j => j.IsInFlight) is { } inFlight)
            throw new BusinessException(ErrorCodes.PublicationTooLateToCancel,
                $"Publishing of this version has already started ({inFlight.DeliveryState}); it can no longer be edited. Wait for the outcome.");
        return jobs.Where(j => j.CanCancel).ToList();
    }

    /// <summary>Cancels the jobs returned by <see cref="JobsToCancelForEditAsync"/> in the same unit of work as the new version.</summary>
    public void CancelForEdit(IReadOnlyList<PublicationJob> jobs, Guid newVersionId, Guid userId)
    {
        foreach (var job in jobs)
        {
            job.Cancel(userId, Now);
            activity.Audit(job.OrganizationId, userId, AuditActions.PublicationCanceled, JobResource, job.Id, new Dictionary<string, string?>
            {
                ["postId"] = job.PostId.ToString(), ["postVersionId"] = job.PostVersionId.ToString(), ["reason"] = "edited",
                ["newPostVersionId"] = newVersionId.ToString(),
            });
        }
    }

    // ---- Helpers --------------------------------------------------------------------------------------------

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    private PublishTime Resolve(LocalSchedule? schedule) => schedule is null
        ? PublishTime.Now(Now)
        : PublishTime.Resolve(schedule.Date, schedule.Time, schedule.TimeZone, schedule.UtcOffsetMinutes, Now);

    /// <summary>Repeated Schedule/Publish Now for the same version and destination never creates a second intent.</summary>
    private async Task<PublicationJobDto> ReuseAsync(OrganizationAccess access, PublicationJob job, PublishTime time, bool publishNow, CancellationToken cancellationToken)
    {
        // Same request again (double click, retry): answer with the existing intent.
        var alreadyDue = publishNow && job.DeliveryState == DeliveryState.SCHEDULED && job.PublishAtUtc <= Now;
        if ((job.DeliveryState == DeliveryState.SCHEDULED && job.IsSameSchedule(time)) || alreadyDue)
            return await ToDtoAsync(access, job, cancellationToken);
        // Publish Now on a scheduled intent moves it forward; a canceled intent, or one the provider refused (nothing was
        // published), is reactivated. Anything else (published, processing, uncertain) conflicts.
        if (job.DeliveryState is DeliveryState.CANCELED or DeliveryState.ACTION_REQUIRED || (publishNow && job.DeliveryState == DeliveryState.SCHEDULED))
        {
            await MoveAsync(access, job, time, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return await ToDtoAsync(access, job, cancellationToken);
        }
        throw Conflict(job);
    }

    private static BusinessException Conflict(PublicationJob job) => job.DeliveryState == DeliveryState.SCHEDULED
        ? new BusinessException(ErrorCodes.IdempotencyConflict,
            $"This version is already scheduled for {job.PublishAtUtc:yyyy-MM-dd HH:mm} UTC. Reschedule that publication instead.")
        : new BusinessException(ErrorCodes.IdempotencyConflict,
            $"This version already has a publication ({job.DeliveryState}); each approved version is published at most once. Edit the post to publish new content.");

    private async Task MoveAsync(OrganizationAccess access, PublicationJob job, PublishTime time, CancellationToken cancellationToken)
    {
        var post = await LoadPostAsync(access, job.PostId, cancellationToken);
        if (post.CurrentVersionId != job.PostVersionId || post.ActiveApprovalId != job.ReviewRequestId)
            throw new BusinessException(ErrorCodes.ApprovalStale, "The post has changed since this publication was approved; it cannot be rescheduled.");
        var (_, _, account, _) = await LoadApprovedAsync(access, post, cancellationToken);
        if (account.ExecutionMode != job.ExecutionMode)
            throw new BusinessException(ErrorCodes.SocialAccountNotReady,
                $"The destination is now {account.ExecutionMode} but this publication was created as {job.ExecutionMode}. Schedule a new version instead.");
        job.Reschedule(time, Now);
        AuditSchedule(access, AuditActions.PublicationRescheduled, job, publishNow: time.TimeZoneId is null);
    }

    /// <summary>Loads and re-verifies everything an approved current version needs to become a publication intent.</summary>
    private async Task<(PostVersion, ReviewRequest, SocialAccount, ApprovalPayload)> LoadApprovedAsync(
        OrganizationAccess access, Post post, CancellationToken cancellationToken)
    {
        if (post.EditorialState != EditorialState.APPROVED || post.ActiveApprovalId is not { } approvalId)
            throw new BusinessException(ErrorCodes.ApprovalRequired, $"Only approved content can be scheduled; this post is {post.EditorialState}.");
        var approval = await reviews.GetAsync(approvalId, cancellationToken) ?? throw new BusinessException(ErrorCodes.ApprovalStale, "The approval no longer exists.");
        access.EnsureOwns(approval.OrganizationId, "Approval");
        var version = await posts.GetVersionAsync(access.OrganizationId, post.Id, post.CurrentVersionId!.Value, cancellationToken)
            ?? throw new InvalidOperationException($"Post {post.Id} has no current version row.");

        var account = version.SocialAccountId is { } accountId ? await socialAccounts.GetAsync(accountId, cancellationToken) : null;
        if (account is null || account.OrganizationId != access.OrganizationId)
            throw new BusinessException(ErrorCodes.SocialAccountNotReady, "The approved destination no longer exists in this workspace.");

        MediaAsset? image = null;
        if (version.MediaAssetId is { } mediaId)
        {
            image = await media.GetAsync(mediaId, cancellationToken);
            if (image is null || image.OrganizationId != access.OrganizationId)
                throw new BusinessException(ErrorCodes.ApprovalStale, "The approved image no longer exists.");
            image.EnsureSuitableFor(post.Platform);
        }
        else if (PlatformRules.For(post.Platform).RequiresImage)
        {
            throw new BusinessException(ErrorCodes.ValidationFailed, $"{post.Platform} posts require one image.");
        }
        EnsureCapable(account, image is not null);

        var payload = ApprovalPayload.From(post, version, account, image);
        PublicationJob.EnsureAuthorized(post, version, approval, account, payload);
        return (version, approval, account, payload);
    }

    /// <summary>Connected destinations must have been granted the capability this content needs; simulated ones always can.</summary>
    private static void EnsureCapable(SocialAccount account, bool hasImage)
    {
        if (account.ExecutionMode is not (ExecutionMode.LIVE or ExecutionMode.TEST_ACCOUNT))
            return;
        var needed = hasImage ? "publish_image" : "publish_text";
        if (!account.Capabilities.Contains(needed))
            throw new BusinessException(ErrorCodes.SocialAccountNotReady,
                $"The destination \"{account.DisplayName}\" was not granted permission to {needed.Replace('_', ' ')}. Reconnect it with the required permission.");
    }

    private void AuditSchedule(OrganizationAccess access, string action, PublicationJob job, bool publishNow) =>
        activity.Audit(access.OrganizationId, access.UserId, action, JobResource, job.Id, new Dictionary<string, string?>
        {
            ["postId"] = job.PostId.ToString(), ["postVersionId"] = job.PostVersionId.ToString(), ["reviewRequestId"] = job.ReviewRequestId.ToString(),
            ["socialAccountId"] = job.SocialAccountId.ToString(), ["executionMode"] = job.ExecutionMode.ToString(),
            ["publishAtUtc"] = job.PublishAtUtc.ToString("O"), ["localPublishAt"] = job.LocalPublishAt?.ToString("yyyy-MM-ddTHH:mm"),
            ["timeZone"] = job.TimeZoneId, ["publishNow"] = publishNow.ToString(), ["payloadHash"] = job.PayloadHash,
        });

    private async Task<Post> LoadPostAsync(OrganizationAccess access, Guid postId, CancellationToken cancellationToken)
    {
        var post = await posts.GetAsync(postId, cancellationToken) ?? throw new BusinessException(ErrorCodes.NotFound, "Post not found.");
        access.EnsureOwns(post.OrganizationId, "Post");
        return post;
    }

    private async Task<PublicationJob> LoadJobAsync(OrganizationAccess access, Guid jobId, CancellationToken cancellationToken)
    {
        var job = await publications.GetAsync(jobId, cancellationToken) ?? throw new BusinessException(ErrorCodes.NotFound, "Publication not found.");
        access.EnsureOwns(job.OrganizationId, "Publication");
        return job;
    }

    private async Task<PublicationJobDto> ToDtoAsync(OrganizationAccess access, PublicationJob job, CancellationToken cancellationToken) =>
        (await publications.ListAsync(access.OrganizationId, new PublicationQuery(PostId: job.PostId), cancellationToken)).Single(j => j.Id == job.Id);
}
