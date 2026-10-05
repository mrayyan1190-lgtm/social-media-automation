using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Application.Notifications.DTOs;

public sealed record NotificationDto(
    Guid Id, NotificationType Type, string Title, string Message, string ResourceType, Guid ResourceId, DateTime CreatedAtUtc, DateTime? ReadAtUtc);

