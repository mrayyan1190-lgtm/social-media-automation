using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.Notifications.DTOs;
using SocialMediaAutomation.Application.Common.Authorization;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Application.Notifications.Services;

/// <summary>The signed-in user's own notifications in one workspace. Nobody can read or mark another user's notifications.</summary>
public sealed class NotificationService(OrganizationAuthorizer authorizer, IActivityRepository activity, IUnitOfWork unitOfWork, TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<NotificationDto>> ListAsync(Guid organizationId, bool unreadOnly, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ViewOrganization, cancellationToken);
        return await activity.ListNotificationsAsync(access.OrganizationId, access.UserId, unreadOnly, take: 50, cancellationToken);
    }

    public async Task<int> CountUnreadAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ViewOrganization, cancellationToken);
        return await activity.CountUnreadAsync(access.OrganizationId, access.UserId, cancellationToken);
    }

    public async Task MarkReadAsync(Guid organizationId, Guid notificationId, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ViewOrganization, cancellationToken);
        var notification = await activity.GetNotificationAsync(access.OrganizationId, access.UserId, notificationId, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.NotFound, "Notification not found.");
        notification.MarkRead(timeProvider.GetUtcNow().UtcDateTime);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkAllReadAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ViewOrganization, cancellationToken);
        await activity.MarkAllReadAsync(access.OrganizationId, access.UserId, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
    }
}
