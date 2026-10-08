using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.Audit.DTOs;
using SocialMediaAutomation.Application.Notifications.DTOs;
using SocialMediaAutomation.Application.Reviews.DTOs;
using SocialMediaAutomation.Application.SocialAccounts.DTOs;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Infrastructure.Persistence.Repositories;

internal sealed class SocialAccountRepository(CoreDbContext db) : ISocialAccountRepository
{
    public Task<SocialAccount?> GetAsync(Guid socialAccountId, CancellationToken cancellationToken) =>
        db.SocialAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == socialAccountId, cancellationToken);

    public async Task<IReadOnlyList<SocialAccountDto>> ListAsync(Guid organizationId, SocialPlatform? platform, CancellationToken cancellationToken) =>
        (await db.SocialAccounts.AsNoTracking()
            .Where(a => a.OrganizationId == organizationId && (platform == null || a.Platform == platform))
            .OrderBy(a => a.Platform).ThenBy(a => a.DisplayName)
            .ToListAsync(cancellationToken))
        .Select(SocialAccountDto.From).ToList();

    public Task<SocialAccount?> FindByExternalIdAsync(Guid organizationId, SocialPlatform platform, string externalAccountId, CancellationToken cancellationToken) =>
        db.SocialAccounts.SingleOrDefaultAsync(
            a => a.OrganizationId == organizationId && a.Platform == platform && a.ExternalAccountId == externalAccountId, cancellationToken);

    public async Task<IReadOnlyList<SocialAccount>> ListByConnectionAsync(Guid organizationId, Guid connectionId, CancellationToken cancellationToken) =>
        await db.SocialAccounts.Where(a => a.OrganizationId == organizationId && a.OAuthConnectionId == connectionId).ToListAsync(cancellationToken);

    public void Add(SocialAccount account) => db.SocialAccounts.Add(account);
}

internal sealed class OAuthRepository(CoreDbContext db) : IOAuthRepository
{
    public void Add(OAuthState state) => db.OAuthStates.Add(state);

    public Task<OAuthState?> FindStateAsync(string stateHash, CancellationToken cancellationToken) =>
        db.OAuthStates.SingleOrDefaultAsync(s => s.StateHash == stateHash, cancellationToken);

    public void Add(OAuthConnection connection) => db.OAuthConnections.Add(connection);

    public Task<OAuthConnection?> GetConnectionAsync(Guid connectionId, CancellationToken cancellationToken) =>
        db.OAuthConnections.SingleOrDefaultAsync(c => c.Id == connectionId, cancellationToken);

    public Task<OAuthConnection?> FindConnectionAsync(Guid organizationId, SocialPlatform platform, string externalUserId, CancellationToken cancellationToken) =>
        db.OAuthConnections.SingleOrDefaultAsync(
            c => c.OrganizationId == organizationId && c.Platform == platform && c.ExternalUserId == externalUserId, cancellationToken);

    public async Task<IReadOnlyList<SocialConnectionDto>> ListConnectionsAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await db.OAuthConnections.AsNoTracking()
            .Where(c => c.OrganizationId == organizationId)
            .OrderBy(c => c.Platform).ThenBy(c => c.DisplayName)
            .Select(c => new SocialConnectionDto(c.Id, c.Platform, c.DisplayName, c.Status, c.Scopes, c.ConnectedAtUtc, c.AccessTokenExpiresAtUtc,
                db.SocialAccounts.Count(a => a.OrganizationId == organizationId && a.OAuthConnectionId == c.Id)))
            .ToListAsync(cancellationToken);
}

internal sealed class ReviewRepository(CoreDbContext db) : IReviewRepository
{
    public Task<ReviewRequest?> GetAsync(Guid reviewRequestId, CancellationToken cancellationToken) =>
        db.ReviewRequests.SingleOrDefaultAsync(r => r.Id == reviewRequestId, cancellationToken);

    public async Task<IReadOnlyList<ReviewSummaryDto>> ListAsync(Guid organizationId, ReviewStatus? status, Guid? postId, CancellationToken cancellationToken) =>
        await (from r in db.ReviewRequests.AsNoTracking()
               join p in db.Posts on r.PostId equals p.Id
               join v in db.PostVersions on r.PostVersionId equals v.Id
               join s in db.Users on r.SubmittedByUserId equals s.Id
               join a in db.Users on r.AssignedReviewerUserId equals a.Id into assignees
               from a in assignees.DefaultIfEmpty()
               join d in db.Users on r.DecidedByUserId equals d.Id into deciders
               from d in deciders.DefaultIfEmpty()
               where r.OrganizationId == organizationId && (status == null || r.Status == status) && (postId == null || r.PostId == postId)
               orderby r.SubmittedAtUtc descending
               select new ReviewSummaryDto(r.Id, r.PostId, r.PostVersionId, r.VersionNumber, p.Platform,
                   (v.Hook ?? v.Body).Length > 140 ? (v.Hook ?? v.Body).Substring(0, 140) : (v.Hook ?? v.Body),
                   r.Status, r.SubmittedByUserId, s.DisplayName, r.AssignedReviewerUserId, a == null ? null : a.DisplayName,
                   r.SubmissionNote, r.SubmittedAtUtc, r.DecidedByUserId, d == null ? null : d.DisplayName,
                   r.DecidedAtUtc, r.DecisionComment, r.ApprovedPayloadHash, r.RowVersion))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ReviewCommentDto>> ListCommentsAsync(Guid organizationId, Guid reviewRequestId, CancellationToken cancellationToken) =>
        await (from c in db.ReviewComments.AsNoTracking()
               join u in db.Users on c.AuthorUserId equals u.Id
               where c.OrganizationId == organizationId && c.ReviewRequestId == reviewRequestId
               orderby c.CreatedAtUtc
               select new ReviewCommentDto(c.Id, c.ReviewRequestId, c.AuthorUserId, u.DisplayName, c.Body, c.CreatedAtUtc))
            .ToListAsync(cancellationToken);

    public void Add(ReviewRequest review) => db.ReviewRequests.Add(review);

    public void Add(ReviewComment comment) => db.ReviewComments.Add(comment);
}

internal sealed class ActivityRepository(CoreDbContext db) : IActivityRepository
{
    public void Add(Notification notification) => db.Notifications.Add(notification);

    public void Add(AuditLog entry) => db.AuditLogs.Add(entry);

    public async Task<IReadOnlyList<NotificationDto>> ListNotificationsAsync(Guid organizationId, Guid userId, bool unreadOnly, int take, CancellationToken cancellationToken) =>
        await db.Notifications.AsNoTracking()
            .Where(n => n.OrganizationId == organizationId && n.RecipientUserId == userId && (!unreadOnly || n.ReadAtUtc == null))
            .OrderByDescending(n => n.CreatedAtUtc).Take(take)
            .Select(n => new NotificationDto(n.Id, n.Type, n.Title, n.Message, n.ResourceType, n.ResourceId, n.CreatedAtUtc, n.ReadAtUtc))
            .ToListAsync(cancellationToken);

    public Task<int> CountUnreadAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) =>
        db.Notifications.CountAsync(n => n.OrganizationId == organizationId && n.RecipientUserId == userId && n.ReadAtUtc == null, cancellationToken);

    public Task<Notification?> GetNotificationAsync(Guid organizationId, Guid userId, Guid notificationId, CancellationToken cancellationToken) =>
        db.Notifications.SingleOrDefaultAsync(n => n.Id == notificationId && n.OrganizationId == organizationId && n.RecipientUserId == userId, cancellationToken);

    public Task MarkAllReadAsync(Guid organizationId, Guid userId, DateTime utcNow, CancellationToken cancellationToken) =>
        db.Notifications
            .Where(n => n.OrganizationId == organizationId && n.RecipientUserId == userId && n.ReadAtUtc == null)
            .ExecuteUpdateAsync(set => set.SetProperty(n => n.ReadAtUtc, utcNow), cancellationToken);

    public async Task<IReadOnlyList<AuditLogDto>> ListAuditAsync(Guid organizationId, Guid? resourceId, int take, CancellationToken cancellationToken) =>
        (await (from a in db.AuditLogs.AsNoTracking()
                join u in db.Users on a.ActorUserId equals u.Id into actors
                from u in actors.DefaultIfEmpty()
                where a.OrganizationId == organizationId && (resourceId == null || a.ResourceId == resourceId)
                orderby a.CreatedAtUtc descending
                select new { a, ActorName = u == null ? null : u.DisplayName })
            .Take(take).ToListAsync(cancellationToken))
        .Select(x => new AuditLogDto(x.a.Id, x.a.ActorUserId, x.ActorName, x.a.Action, x.a.ResourceType, x.a.ResourceId,
            JsonSerializer.Deserialize<Dictionary<string, string?>>(x.a.MetadataJson) ?? [], x.a.CreatedAtUtc))
        .ToList();
}
