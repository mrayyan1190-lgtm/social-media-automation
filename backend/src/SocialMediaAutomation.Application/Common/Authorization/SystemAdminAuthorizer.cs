using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Application.Common.Authorization;

/// <summary>
/// Internal platform administrators (our operators), flagged on the global User (<c>Users.IsSystemAdmin</c>). This is NOT a
/// workspace role: a workspace Admin is a customer and never gets it. Checked against the database on every call.
/// </summary>
public sealed class SystemAdminAuthorizer(OrganizationAuthorizer authorizer, IUserRepository users)
{
    public async Task<bool> IsSystemAdminAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await users.GetAsync(userId, cancellationToken);
        return user is { IsActive: true, IsSystemAdmin: true };
    }

    public async Task RequireAsync(CancellationToken cancellationToken)
    {
        if (!await IsSystemAdminAsync(authorizer.RequireUserId(), cancellationToken))
            throw new BusinessException(ErrorCodes.Forbidden, "This area is restricted to internal system administrators.");
    }
}
