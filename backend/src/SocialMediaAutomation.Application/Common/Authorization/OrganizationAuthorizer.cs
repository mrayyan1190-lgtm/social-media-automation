using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Application.Common.Authorization;

/// <summary>
/// Resolves authenticated user -> active membership -> permission for an organization.
/// Every tenant-scoped use case starts here; knowing an organization id grants nothing.
/// </summary>
public sealed class OrganizationAuthorizer(ICurrentUser currentUser, IOrganizationRepository organizations)
{
    public Guid RequireUserId() =>
        currentUser.UserId ?? throw new BusinessException(ErrorCodes.Unauthorized, "Authentication is required.");

    public async Task<OrganizationAccess> AuthorizeAsync(Guid organizationId, Permission permission, CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        var membership = await organizations.GetMembershipAsync(organizationId, userId, cancellationToken);

        // Non-members and missing organizations get the same answer so organization ids cannot be probed.
        if (membership is null || !membership.IsActive)
            throw new BusinessException(ErrorCodes.NotFound, "Organization not found.");

        // Members of a workspace suspended by the platform operator lose access until it is reactivated.
        if (await organizations.GetAsync(organizationId, cancellationToken) is { Status: OrganizationStatus.Suspended })
            throw new BusinessException(ErrorCodes.WorkspaceSuspended, "This workspace is suspended. Contact support.");

        if (!membership.Has(permission))
            throw new BusinessException(ErrorCodes.Forbidden, $"Your role ({membership.Role}) does not allow {permission}.");

        return new OrganizationAccess(organizationId, userId, membership.Id, membership.Role);
    }
}
