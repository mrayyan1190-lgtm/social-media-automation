using System.Security.Claims;
using SocialMediaAutomation.Application.Abstractions;

namespace SocialMediaAutomation.Api.Authorization;

/// <summary>Reads the user id from the authenticated session cookie. Organization context is never taken from the cookie.</summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public Guid? UserId =>
        Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
