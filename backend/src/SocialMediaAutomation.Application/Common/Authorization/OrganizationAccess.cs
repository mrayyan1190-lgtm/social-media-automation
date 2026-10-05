using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Application.Common.Authorization;

/// <summary>Proof that the current user holds a permission in one organization. Obtain only from <see cref="OrganizationAuthorizer"/>.</summary>
public sealed record OrganizationAccess(Guid OrganizationId, Guid UserId, Guid MemberId, OrganizationRole Role)
{
    /// <summary>
    /// Final step of the tenant check: a resource loaded by id must belong to the authorized organization.
    /// Reported as NOT_FOUND so another workspace's ids cannot be probed.
    /// </summary>
    public void EnsureOwns(Guid resourceOrganizationId, string resourceName)
    {
        if (resourceOrganizationId != OrganizationId)
            throw new BusinessException(ErrorCodes.NotFound, $"{resourceName} not found.");
    }
}
