using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using SocialMediaAutomation.Application.Audit.DTOs;
using SocialMediaAutomation.Application.Audit.Services;
using SocialMediaAutomation.Application.Notifications.DTOs;
using SocialMediaAutomation.Application.Notifications.Services;
using SocialMediaAutomation.Application.SocialAccounts.DTOs;
using SocialMediaAutomation.Application.SocialAccounts.Services;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Api.Controllers;

public sealed record CreateSocialAccountRequest([Required] SocialPlatform? Platform, string DisplayName, [Required] ExecutionMode? ExecutionMode);

public sealed record CreateSimulatedAccountRequest([Required] SocialPlatform? Platform, string? DisplayName);

[ApiController]
[Route("api/v1/organizations/{orgId:guid}/social-accounts")]
public sealed class SocialAccountsController(SocialAccountService accounts) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<SocialAccountDto>> List(Guid orgId, [FromQuery] SocialPlatform? platform, CancellationToken cancellationToken) =>
        accounts.ListAsync(orgId, platform, cancellationToken);

    /// <summary>Admin. Creates a SIMULATED or NOT_CONFIGURED destination; LIVE/TEST destinations come from provider connections.</summary>
    [HttpPost]
    public async Task<ActionResult<SocialAccountDto>> Create(Guid orgId, CreateSocialAccountRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created,
            await accounts.CreateUnconnectedAsync(orgId, request.Platform!.Value, request.DisplayName, request.ExecutionMode!.Value, cancellationToken));

    /// <summary>Admin. A clearly labelled SIMULATED destination, so the workflow can run without provider access.</summary>
    [HttpPost("simulated")]
    public async Task<ActionResult<SocialAccountDto>> CreateSimulated(Guid orgId, CreateSimulatedAccountRequest request, CancellationToken cancellationToken)
    {
        var platform = request.Platform!.Value;
        var name = string.IsNullOrWhiteSpace(request.DisplayName) ? $"{platform} (simulated)" : request.DisplayName;
        return StatusCode(StatusCodes.Status201Created,
            await accounts.CreateUnconnectedAsync(orgId, platform, name, ExecutionMode.SIMULATED, cancellationToken));
    }
}

[ApiController]
[Route("api/v1/organizations/{orgId:guid}/notifications")]
public sealed class NotificationsController(NotificationService notifications) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<NotificationDto>> List(Guid orgId, [FromQuery] bool unreadOnly, CancellationToken cancellationToken) =>
        notifications.ListAsync(orgId, unreadOnly, cancellationToken);

    [HttpGet("unread-count")]
    public async Task<object> UnreadCount(Guid orgId, CancellationToken cancellationToken) =>
        new { count = await notifications.CountUnreadAsync(orgId, cancellationToken) };

    [HttpPost("{notificationId:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid orgId, Guid notificationId, CancellationToken cancellationToken)
    {
        await notifications.MarkReadAsync(orgId, notificationId, cancellationToken);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(Guid orgId, CancellationToken cancellationToken)
    {
        await notifications.MarkAllReadAsync(orgId, cancellationToken);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1/organizations/{orgId:guid}/audit")]
public sealed class AuditController(AuditService audit) : ControllerBase
{
    /// <summary>Admin. Newest first; optionally for one resource (e.g. a post or review).</summary>
    [HttpGet]
    public Task<IReadOnlyList<AuditLogDto>> List(Guid orgId, [FromQuery] Guid? resourceId, CancellationToken cancellationToken) =>
        audit.ListAsync(orgId, resourceId, cancellationToken);
}
