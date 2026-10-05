using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SocialMediaAutomation.Api.Extensions;
using SocialMediaAutomation.Application.SocialAccounts.DTOs;
using SocialMediaAutomation.Application.SocialAccounts.Services;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Api.Controllers;

public sealed record StartConnectionRequest([Required] ExecutionMode? Mode);

public sealed record StartConnectionResponse(string AuthorizationUrl);

[ApiController]
[Route("api/v1/organizations/{orgId:guid}/social-connections")]
public sealed class SocialConnectionsController(SocialConnectionService connections) : ControllerBase
{
    /// <summary>Which providers can be connected live on this server, and why not when blocked.</summary>
    [HttpGet("providers")]
    public Task<IReadOnlyList<SocialProviderDto>> Providers(Guid orgId, CancellationToken cancellationToken) =>
        connections.ListProvidersAsync(orgId, cancellationToken);

    /// <summary>Connection metadata only; token material never leaves the server.</summary>
    [HttpGet]
    public Task<IReadOnlyList<SocialConnectionDto>> List(Guid orgId, CancellationToken cancellationToken) =>
        connections.ListConnectionsAsync(orgId, cancellationToken);

    /// <summary>Admin. Returns the provider authorization URL; the browser navigates there and the provider calls back.</summary>
    [EnableRateLimiting(RateLimitingExtensions.OAuth)]
    [HttpPost("{platform}/start")]
    public async Task<StartConnectionResponse> Start(Guid orgId, SocialPlatform platform, StartConnectionRequest request, CancellationToken cancellationToken) =>
        new(await connections.StartAsync(orgId, platform, request.Mode!.Value, cancellationToken));

    [HttpPost("{connectionId:guid}/disconnect")]
    public async Task<IActionResult> Disconnect(Guid orgId, Guid connectionId, CancellationToken cancellationToken)
    {
        await connections.DisconnectAsync(orgId, connectionId, cancellationToken);
        return NoContent();
    }
}

/// <summary>
/// Provider redirect target. Anonymous because the SameSite=Strict session cookie is not sent on a cross-site redirect;
/// the one-time state identifies user and workspace. Always redirects to a fixed web page (no open redirect).
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/oauth/{platform}/callback")]
public sealed class OAuthCallbackController(SocialConnectionService connections, IConfiguration configuration) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Callback(
        SocialPlatform platform, [FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error, CancellationToken cancellationToken)
    {
        var result = await connections.CompleteAsync(platform, state, code, error, cancellationToken);
        var webBase = (configuration["App:WebBaseUrl"] ?? "").TrimEnd('/');
        return Redirect($"{webBase}/social-accounts?status={(result.Succeeded ? "connected" : "failed")}" +
            $"&message={Uri.EscapeDataString(result.Message)}");
    }
}
