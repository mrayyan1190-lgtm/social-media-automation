using Microsoft.EntityFrameworkCore;
using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.Publications.DTOs;
using SocialMediaAutomation.Domain.Entities;

namespace SocialMediaAutomation.Infrastructure.Persistence.Repositories;

internal sealed class PublicationRepository(CoreDbContext db) : IPublicationRepository
{
    private static readonly DeliveryState[] Finished = [DeliveryState.PUBLISHED, DeliveryState.SIMULATED, DeliveryState.FAILED, DeliveryState.CANCELED];

    public void Add(PublicationJob job) => db.PublicationJobs.Add(job);

    public Task<PublicationJob?> GetAsync(Guid jobId, CancellationToken cancellationToken) =>
        db.PublicationJobs.SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken);

    public Task<PublicationJob?> FindIntentAsync(Guid organizationId, Guid postVersionId, Guid socialAccountId, CancellationToken cancellationToken) =>
        db.PublicationJobs.SingleOrDefaultAsync(
            j => j.OrganizationId == organizationId && j.PostVersionId == postVersionId && j.SocialAccountId == socialAccountId, cancellationToken);

    public async Task<IReadOnlyList<PublicationJob>> ListForVersionAsync(Guid organizationId, Guid postVersionId, CancellationToken cancellationToken) =>
        await db.PublicationJobs.Where(j => j.OrganizationId == organizationId && j.PostVersionId == postVersionId).ToListAsync(cancellationToken);

    public void Add(PublicationAttempt attempt) => db.PublicationAttempts.Add(attempt);

    public async Task<IReadOnlyList<PublicationJob>> ListExpiredLeasesAsync(DateTime utcNow, int take, CancellationToken cancellationToken) =>
        await db.PublicationJobs.Where(j => j.DeliveryState == DeliveryState.PROCESSING && j.LeaseExpiresAtUtc < utcNow)
            .OrderBy(j => j.LeaseExpiresAtUtc).Take(take).ToListAsync(cancellationToken);

    public Task<PublicationAttempt?> FindOpenAttemptAsync(Guid jobId, CancellationToken cancellationToken) =>
        db.PublicationAttempts.Where(a => a.PublicationJobId == jobId && a.FinishedAtUtc == null)
            .OrderByDescending(a => a.StartedAtUtc).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<PublicationAttemptDto>> ListAttemptsAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken) =>
        await db.PublicationAttempts.AsNoTracking()
            .Where(a => a.OrganizationId == organizationId && a.PublicationJobId == jobId)
            .OrderBy(a => a.StartedAtUtc)
            .Select(a => new PublicationAttemptDto(a.Id, a.AttemptNumber, a.Step, a.StartedAtUtc, a.FinishedAtUtc, a.ResultState, a.ErrorCategory,
                a.ErrorCode, a.ErrorMessage, a.ProviderRequestId, a.ExternalId, a.Detail))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<DeliveryState, int>> CountByDeliveryStateAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await db.PublicationJobs.Where(j => j.OrganizationId == organizationId)
            .GroupBy(j => j.DeliveryState).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);

    public async Task<IReadOnlyList<PublicationJobDto>> ListAsync(Guid organizationId, PublicationQuery query, CancellationToken cancellationToken)
    {
        var jobs = db.PublicationJobs.AsNoTracking().Where(j => j.OrganizationId == organizationId);
        if (query.PostId is { } postId)
            jobs = jobs.Where(j => j.PostId == postId);
        if (query.OpenOnly)
            jobs = jobs.Where(j => !Finished.Contains(j.DeliveryState));
        if (query.FromUtc is { } from)
            jobs = jobs.Where(j => j.PublishAtUtc >= from);
        if (query.ToUtc is { } to)
            jobs = jobs.Where(j => j.PublishAtUtc < to);
        if (query.Platform is { } platform)
            jobs = jobs.Where(j => j.Platform == platform);
        if (query.States is { Count: > 0 } states)
            jobs = jobs.Where(j => states.Contains(j.DeliveryState));
        jobs = query.SoonestFirst ? jobs.OrderBy(j => j.PublishAtUtc) : jobs.OrderByDescending(j => j.PublishAtUtc);
        var rows = await (from j in jobs.Take(query.Take)
               join v in db.PostVersions on j.PostVersionId equals v.Id
               join p in db.Posts on j.PostId equals p.Id
               join c in db.Campaigns on p.CampaignId equals c.Id
               join a in db.SocialAccounts on j.SocialAccountId equals a.Id
               select new PublicationJobDto(j.Id, j.PostId, j.PostVersionId, v.VersionNumber, c.Id, c.Name, j.Platform, j.SocialAccountId, a.DisplayName,
                   j.ExecutionMode, j.DeliveryState, j.PublishAtUtc, j.LocalPublishAt, j.TimeZoneId, j.UtcOffsetMinutes, j.PayloadHash,
                   j.AttemptCount, j.MaxAttempts, j.ExternalPostId, j.ExternalPostUrl, j.LastErrorCode, j.LastErrorMessage, j.CreatedAtUtc, j.CanceledAtUtc,
                   j.DeliveryState == DeliveryState.SCHEDULED || j.DeliveryState == DeliveryState.RETRY_PENDING || j.DeliveryState == DeliveryState.ACTION_REQUIRED,
                   j.DeliveryState == DeliveryState.SCHEDULED || j.DeliveryState == DeliveryState.CANCELED,
                   j.RowVersion))
            .ToListAsync(cancellationToken);
        // Joins do not guarantee the order of the limited subquery; sort the (bounded) result explicitly.
        return query.SoonestFirst ? rows.OrderBy(r => r.PublishAtUtc).ToList() : rows.OrderByDescending(r => r.PublishAtUtc).ToList();
    }
}
