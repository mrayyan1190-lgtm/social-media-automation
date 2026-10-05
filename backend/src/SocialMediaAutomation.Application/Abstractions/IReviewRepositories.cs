using SocialMediaAutomation.Application.Audit.DTOs;
using SocialMediaAutomation.Application.Notifications.DTOs;
using SocialMediaAutomation.Application.Reviews.DTOs;
using SocialMediaAutomation.Application.SocialAccounts.DTOs;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Application.Abstractions;

public interface ISocialAccountRepository
{
    /// <summary>Loads by id only; callers must verify OrganizationId.</summary>
    Task<SocialAccount?> GetAsync(Guid socialAccountId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SocialAccountDto>> ListAsync(Guid organizationId, SocialPlatform? platform, CancellationToken cancellationToken);

    /// <summary>Tracked; the destination for a provider account in this workspace, if it already exists.</summary>
    Task<SocialAccount?> FindByExternalIdAsync(Guid organizationId, SocialPlatform platform, string externalAccountId, CancellationToken cancellationToken);

    /// <summary>Tracked; destinations acting through the given grant.</summary>
    Task<IReadOnlyList<SocialAccount>> ListByConnectionAsync(Guid organizationId, Guid connectionId, CancellationToken cancellationToken);

    void Add(SocialAccount account);
}

public interface IReviewRepository
{
    /// <summary>Tracked; loads by id only, so callers verify OrganizationId.</summary>
    Task<ReviewRequest?> GetAsync(Guid reviewRequestId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ReviewSummaryDto>> ListAsync(Guid organizationId, ReviewStatus? status, Guid? postId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ReviewCommentDto>> ListCommentsAsync(Guid organizationId, Guid reviewRequestId, CancellationToken cancellationToken);

    void Add(ReviewRequest review);

    void Add(ReviewComment comment);
}

public interface IActivityRepository
{
    void Add(Notification notification);

    void Add(AuditLog entry);

    Task<IReadOnlyList<NotificationDto>> ListNotificationsAsync(Guid organizationId, Guid userId, bool unreadOnly, int take, CancellationToken cancellationToken);

    Task<int> CountUnreadAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken);

    /// <summary>Tracked; the notification only if it belongs to this recipient in this organization.</summary>
    Task<Notification?> GetNotificationAsync(Guid organizationId, Guid userId, Guid notificationId, CancellationToken cancellationToken);

    Task MarkAllReadAsync(Guid organizationId, Guid userId, DateTime utcNow, CancellationToken cancellationToken);

    Task<IReadOnlyList<AuditLogDto>> ListAuditAsync(Guid organizationId, Guid? resourceId, int take, CancellationToken cancellationToken);
}
