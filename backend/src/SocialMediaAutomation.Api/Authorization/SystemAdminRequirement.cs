using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using SocialMediaAutomation.Application.Common.Authorization;
using SocialMediaAutomation.Domain.Constants;

namespace SocialMediaAutomation.Api.Authorization;

/// <summary>
/// RequireSystemAdmin: the session must come from the internal sign-in (system-role claim) AND the user must still be an
/// active system administrator in the database, so revoking the flag takes effect on the next request.
/// A workspace Admin fails both checks.
/// </summary>
internal sealed class SystemAdminRequirement : IAuthorizationRequirement;

internal sealed class SystemAdminHandler(SystemAdminAuthorizer systemAdmins) : AuthorizationHandler<SystemAdminRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, SystemAdminRequirement requirement)
    {
        if (context.User.HasClaim(SystemRoles.ClaimType, SystemRoles.SystemAdmin)
            && Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            && await systemAdmins.IsSystemAdminAsync(userId, CancellationToken.None))
            context.Succeed(requirement);
    }
}
