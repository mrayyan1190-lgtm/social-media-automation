using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Domain.Entities;

namespace SocialMediaAutomation.Application.Platform;

/// <summary>
/// Records operator actions with actor, target, result and correlation id in the current unit of work. Callers pass
/// identifiers and states only; the entity rejects metadata keys that look like secrets.
/// </summary>
public sealed class PlatformAudit(IPlatformRepository platform, ICurrentUser currentUser, ICorrelationContext correlation, TimeProvider timeProvider)
{
    public void Record(string action, string targetType, string targetId, string? targetLabel, Guid? organizationId = null, bool succeeded = true,
        IReadOnlyDictionary<string, string?>? metadata = null, Guid? actorUserId = null) =>
        platform.Add(PlatformAuditLog.Record(actorUserId ?? currentUser.UserId, action, targetType, targetId, targetLabel, organizationId, succeeded,
            correlation.CorrelationId, metadata, timeProvider.GetUtcNow().UtcDateTime));
}
