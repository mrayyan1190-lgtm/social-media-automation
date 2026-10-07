using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SocialMediaAutomation.Api.Extensions;
using SocialMediaAutomation.Api.Contracts;
using SocialMediaAutomation.Api.Middleware;
using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.Authentication.DTOs;
using SocialMediaAutomation.Application.Authentication.Services;
using SocialMediaAutomation.Application.Platform;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(AccountService accounts, SignInRecorder signIns, ICurrentUser currentUser) : ControllerBase
{
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingExtensions.Login)]
    [HttpPost("login")]
    public async Task<MeDto> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var (userId, knownUserId, failure) = await accounts.CheckCredentialsAsync(request.Email, request.Password, cancellationToken);
        await signIns.RecordAsync(knownUserId, request.Email, SignInRecorder.CustomerArea, failure ?? SignInEvents.Succeeded, SignInClient.From(HttpContext),
            cancellationToken);
        // One generic answer whether the email is unknown, the password wrong or the account disabled.
        if (userId is not { } id)
            throw new BusinessException(ErrorCodes.InvalidCredentials, "Invalid email or password.");

        await SignInAsync(id);
        return await accounts.GetMeAsync(id, cancellationToken);
    }

    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingExtensions.Login)]
    [HttpPost("register")]
    public async Task<ActionResult<RegisteredUserDto>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var user = await accounts.RegisterAsync(new RegisterCommand(request.Email, request.Password, request.WorkspaceName), cancellationToken);
        await SignInAsync(user.Id);
        await signIns.RecordAsync(user.Id, user.Email, SignInRecorder.CustomerArea, SignInEvents.Succeeded, SignInClient.From(HttpContext), cancellationToken);
        return StatusCode(StatusCodes.Status201Created, user);
    }

    /// <summary>Starts "Continue with Google"; Google returns to /auth/google/callback, which the Google handler completes.</summary>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingExtensions.Login)]
    [HttpGet("google")]
    public async Task<IActionResult> Google([FromServices] IAuthenticationSchemeProvider schemes) =>
        await schemes.GetSchemeAsync(GoogleDefaults.AuthenticationScheme) is null
            ? Redirect(GoogleSignIn.LoginPage(HttpContext, "google_not_configured"))
            : Challenge(GoogleDefaults.AuthenticationScheme);

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is { } userId && await accounts.FindEmailAsync(userId, cancellationToken) is { } email)
            await signIns.RecordAsync(userId, email, SignInRecorder.CustomerArea, SignInEvents.LoggedOut, SignInClient.From(HttpContext), cancellationToken);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    private Task SignInAsync(Guid userId) =>
        HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, AuthenticationExtensions.SessionPrincipal(userId));
}
