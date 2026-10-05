using System.Text.Json;
using SocialMediaAutomation.Domain.Common;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Domain.Entities;

/// <summary>In-app workflow feedback for one user in one workspace (PRD §31). Not the audit log.</summary>
public sealed class Notification
{
    private Notification() { }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid RecipientUserId { get; private set; }
    public NotificationType Type { get; private set; }
    public string Title { get; private set; } = null!;
    public string Message { get; private set; } = null!;
    public string ResourceType { get; private set; } = null!;
    public Guid ResourceId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ReadAtUtc { get; private set; }

    public static Notification Create(
        Guid organizationId, Guid recipientUserId, NotificationType type, string title, string message, string resourceType, Guid resourceId, DateTime utcNow) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = organizationId,
        RecipientUserId = recipientUserId,
        Type = type,
        Title = Guard.Required(title, "Title", 200),
        Message = Guard.Required(message, "Message", 1000),
        ResourceType = resourceType,
        ResourceId = resourceId,
        CreatedAtUtc = utcNow,
    };

    public void MarkRead(DateTime utcNow) => ReadAtUtc ??= utcNow;
}

/// <summary>
/// Immutable record of an important business action (PRD §32): who did what to which resource, when, in which workspace.
/// Metadata must be safe: identifiers, versions, hashes and states only, never secrets, tokens or credentials.
/// </summary>
public sealed class AuditLog
{
    private AuditLog() { }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public string Action { get; private set; } = null!;
    public string ResourceType { get; private set; } = null!;
    public Guid ResourceId { get; private set; }
    public string MetadataJson { get; private set; } = "{}";
    public DateTime CreatedAtUtc { get; private set; }

    private static readonly string[] ForbiddenKeyFragments = ["token", "secret", "password", "apikey", "api_key", "authorization", "credential"];

    public static AuditLog Record(
        Guid organizationId, Guid? actorUserId, string action, string resourceType, Guid resourceId, IReadOnlyDictionary<string, string?>? metadata, DateTime utcNow)
    {
        var safe = metadata ?? new Dictionary<string, string?>();
        if (safe.Keys.FirstOrDefault(k => ForbiddenKeyFragments.Any(f => k.Contains(f, StringComparison.OrdinalIgnoreCase))) is { } forbidden)
            throw new InvalidOperationException($"Audit metadata key '{forbidden}' looks like a secret and must not be recorded.");
        return new AuditLog
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ActorUserId = actorUserId,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            MetadataJson = JsonSerializer.Serialize(safe),
            CreatedAtUtc = utcNow,
        };
    }
}
