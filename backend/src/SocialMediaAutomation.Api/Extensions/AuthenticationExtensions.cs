using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using SocialMediaAutomation.Api.Authorization;
using SocialMediaAutomation.Api.Middleware;
using System.Security.Claims;
using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.Authentication.Services;
using SocialMediaAutomation.Application.Platform;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Constants;

namespace SocialMediaAutomation.Api.Extensions;

internal static class AuthenticationExtensions
{
    public const string SessionCookieName = "sma_session";

    /// <summary>
    /// Server-issued HttpOnly session cookie: no auth token is ever readable by browser script or stored in localStorage.
    /// Every endpoint requires an authenticated user unless it opts out with [AllowAnonymous].
    /// </summary>
    public static IServiceCollection AddSessionAuthentication(this IServiceCollection services, IHostEnvironment environment, IConfiguration configuration)
    {
        var authentication = services
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

        // "Continue with Google": the OAuth client comes from platform settings (/internal/social-providers) or the
        // deployment configuration (Authentication:Google), read when the options are built. AuthController refuses to
        // start sign-in without one; the placeholder only satisfies the handler's validation on unrelated requests.
        authentication.AddGoogle(options =>
            {
                options.CallbackPath = "/api/v1/auth/google/callback";
                options.ClaimActions.MapJsonKey("email_verified", "email_verified");
                // Lax, not None: browsers drop SameSite=None cookies on plain-http localhost, which would break the callback.
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                options.Events.OnTicketReceived = GoogleSignIn.CompleteAsync;
                options.Events.OnRemoteFailure = context =>
                {
                    context.Response.Redirect(GoogleSignIn.LoginPage(context.HttpContext, "google_failed"));
                    context.HandleResponse();
                    return Task.CompletedTask;
                };
            });
        services.AddOptions<GoogleOptions>(GoogleDefaults.AuthenticationScheme).Configure<IProviderAppCache>((options, apps) =>
        {
            options.ClientId = apps.GoogleSignIn?.ClientId ?? "not-configured";
            options.ClientSecret = apps.GoogleSignIn?.ClientSecret ?? "not-configured";
        });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(SystemRoles.RequireSystemAdminPolicy, policy => policy.RequireAuthenticatedUser().AddRequirements(new SystemAdminRequirement()));
        services.AddScoped<IAuthorizationHandler, SystemAdminHandler>();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        return services;
    }

    /// <summary>
    /// The session carries the user id, plus the system-role claim for a system administrator so one sign-in opens the
    /// internal area too. Organization access and the system-admin flag are re-checked against the database on every request.
    /// </summary>
    public static ClaimsPrincipal SessionPrincipal(Guid userId, bool isSystemAdmin = false)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        if (isSystemAdmin) claims.Add(new Claim(SystemRoles.ClaimType, SystemRoles.SystemAdmin));
        return new(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }
}

/// <summary>
/// Ends the Google sign-in: the verified email becomes our own session cookie (no Google token is kept), then the browser
/// returns to the web app. Failures land on the login page with a stable error code, never the provider's raw message.
/// </summary>
internal static class GoogleSignIn
{
    public static async Task CompleteAsync(TicketReceivedContext context)
    {
        var http = context.HttpContext;
        var email = context.Principal?.FindFirstValue(ClaimTypes.Email);
        var verified = string.Equals(context.Principal?.FindFirstValue("email_verified"), "true", StringComparison.OrdinalIgnoreCase);
        var accounts = http.RequestServices.GetRequiredService<AccountService>();
        var (userId, knownUserId, failure) = await accounts.SignInWithGoogleAsync(email, verified, http.RequestAborted);
        await http.RequestServices.GetRequiredService<SignInRecorder>().RecordAsync(knownUserId, email ?? "", SignInRecorder.CustomerArea,
            failure ?? SignInEvents.Succeeded, SignInClient.From(http), http.RequestAborted);

        if (userId is { } id)
        {
            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, AuthenticationExtensions.SessionPrincipal(id));
            context.Response.Redirect($"{WebBase(http)}/home");
        }
        else
        {
            context.Response.Redirect(LoginPage(http, failure == SignInEvents.AccountDisabled ? "account_disabled" : "google_email_unverified"));
        }
        context.HandleResponse();
    }

    public static string LoginPage(HttpContext http, string error) => $"{WebBase(http)}/login?error={error}";

    private static string WebBase(HttpContext http) =>
        (http.RequestServices.GetRequiredService<IConfiguration>()["App:WebBaseUrl"] ?? "").TrimEnd('/');
}
