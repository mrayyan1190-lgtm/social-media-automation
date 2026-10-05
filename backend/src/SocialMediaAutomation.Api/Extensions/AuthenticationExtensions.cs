using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using SocialMediaAutomation.Api.Authorization;
using SocialMediaAutomation.Api.Middleware;
using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Domain.Constants;

namespace SocialMediaAutomation.Api.Extensions;

internal static class AuthenticationExtensions
{
    public const string SessionCookieName = "sma_session";

    /// <summary>
    /// Server-issued HttpOnly session cookie: no auth token is ever readable by browser script or stored in localStorage.
    /// Every endpoint requires an authenticated user unless it opts out with [AllowAnonymous].
    /// </summary>
    public static IServiceCollection AddSessionAuthentication(this IServiceCollection services, IHostEnvironment environment)
    {
        services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = SessionCookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = environment.IsProduction() ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;
                // An API answers with status codes, never with redirects to a login page.
                options.Events.OnRedirectToLogin = context =>
                    ProblemResponses.WriteAsync(context.HttpContext, ErrorCodes.Unauthorized, "Authentication is required.");
                options.Events.OnRedirectToAccessDenied = context =>
                    ProblemResponses.WriteAsync(context.HttpContext, ErrorCodes.Forbidden, "Access denied.");
                // A disabled account loses its open sessions immediately, not when the cookie expires.
                // ponytail: one user lookup per authenticated request; cache briefly if it ever shows up in profiles.
                options.Events.OnValidatePrincipal = async context =>
                {
                    var id = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                    var users = context.HttpContext.RequestServices.GetRequiredService<IUserRepository>();
                    if (!Guid.TryParse(id, out var userId) || await users.GetAsync(userId, context.HttpContext.RequestAborted) is not { IsActive: true })
                    {
                        context.RejectPrincipal();
                        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    }
                };
            });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(SystemRoles.RequireSystemAdminPolicy, policy => policy.RequireAuthenticatedUser().AddRequirements(new SystemAdminRequirement()));
        services.AddScoped<IAuthorizationHandler, SystemAdminHandler>();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        return services;
    }
}
