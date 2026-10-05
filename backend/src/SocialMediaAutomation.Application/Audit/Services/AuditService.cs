using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.Audit.DTOs;
using SocialMediaAutomation.Application.Common.Authorization;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Application.Audit.Services;

/// <summary>Read access to the audit trail (Admin). Entries are written only by the business commands themselves.</summary>
public sealed class AuditService(OrganizationAuthorizer authorizer, IActivityRepository activity)
{
    public async Task<IReadOnlyList<AuditLogDto>> ListAsync(Guid organizationId, Guid? resourceId, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ViewAuditLog, cancellationToken);
        return await activity.ListAuditAsync(access.OrganizationId, resourceId, take: 200, cancellationToken);
    }
}
