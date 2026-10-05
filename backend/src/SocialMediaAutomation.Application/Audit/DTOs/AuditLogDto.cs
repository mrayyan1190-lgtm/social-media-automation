namespace SocialMediaAutomation.Application.Audit.DTOs;

public sealed record AuditLogDto(
    Guid Id, Guid? ActorUserId, string? ActorName, string Action, string ResourceType, Guid ResourceId,
    IReadOnlyDictionary<string, string?> Metadata, DateTime CreatedAtUtc);
