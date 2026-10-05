using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SocialMediaAutomation.Api.Contracts;
using SocialMediaAutomation.Api.Extensions;
using SocialMediaAutomation.Api.Middleware;
using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.Authentication.DTOs;
using SocialMediaAutomation.Application.Authentication.Services;
using SocialMediaAutomation.Application.Operations;
using SocialMediaAutomation.Application.Platform;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Api.Controllers;

public sealed record SaveAiConfigurationRequest(
    [Required] AiProvider? Provider, string? BaseUrl, [Required, MaxLength(200)] string Model, [MaxLength(200)] string? FallbackModel,
    int? MaxOutputTokens, int? RequestTimeoutSeconds, bool Enabled, string? RowVersion);

/// <summary>The key is write-only: it is stored in the secret store and never returned.</summary>
public sealed record ReplaceSecretRequest([Required, MaxLength(500)] string Value);

public sealed record TestAiConnectionRequest([Required] AiProvider? Provider, string? BaseUrl, string? Model, string? ApiKey);

public sealed record SaveAiImageConfigurationRequest(
    [Required] AiProvider? Provider, string? BaseUrl, [Required, MaxLength(200)] string Model, [Required, MaxLength(20)] string Size, bool Enabled, string? RowVersion);

public sealed record SaveSocialProviderRequest([Required, MaxLength(200)] string ClientId, [MaxLength(2048)] string? RedirectUri, bool Enabled, string? RowVersion);

public sealed record ConfirmPublishedRequest([MaxLength(200)] string? ExternalPostId, [MaxLength(2048)] string? ExternalPostUrl);

public sealed record ConfirmFailedRequest([Required, MaxLength(500)] string Reason);

/// <summary>Internal system-administration sign-in. Separate from the customer login (/api/v1/auth/login).</summary>
[ApiController]
[Route("api/internal/auth")]
public sealed class InternalAuthController(AccountService accounts, SignInRecorder signIns, PlatformAudit audit, ICurrentUser currentUser) : ControllerBase
{
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingExtensions.Login)]
    [HttpPost("login")]
    public async Task<SystemAdminMeDto> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var client = SignInClient.From(HttpContext);
        var (userId, knownUserId, failure) = await accounts.CheckCredentialsAsync(request.Email, request.Password, cancellationToken);
        if (userId is not { } id)
        {
            await signIns.RecordAsync(knownUserId, request.Email, SignInRecorder.InternalArea, failure!, client, cancellationToken);
            throw new BusinessException(ErrorCodes.InvalidCredentials, "Invalid email or password.");
        }

        SystemAdminMeDto me;
        try
        {
            // Valid credentials but not an operator (e.g. a workspace Admin): FORBIDDEN, and no session is issued.
            me = await accounts.GetSystemAdminAsync(id, cancellationToken);
        }
        catch (BusinessException ex) when (ex.Code == ErrorCodes.Forbidden)
        {
            await signIns.RecordAsync(id, request.Email, SignInRecorder.InternalArea, SignInEvents.InternalForbidden, client, cancellationToken);
            throw;
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(SystemRoles.ClaimType, SystemRoles.SystemAdmin)],
            CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        audit.Record(PlatformAuditActions.AdminSignedIn, "User", id.ToString(), me.Email, actorUserId: id);
        await signIns.RecordAsync(id, me.Email, SignInRecorder.InternalArea, SignInEvents.Succeeded, client, cancellationToken); // saves the audit too
        return me;
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is { } id && await accounts.FindEmailAsync(id, cancellationToken) is { } email)
        {
            audit.Record(PlatformAuditActions.AdminSignedOut, "User", id.ToString(), email);
            await signIns.RecordAsync(id, email, SignInRecorder.InternalArea, SignInEvents.LoggedOut, SignInClient.From(HttpContext), cancellationToken);
        }
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }
}

/// <summary>
/// Internal platform administration. Every endpoint requires the RequireSystemAdmin policy, and every service re-checks
/// the system administrator against the database. Responses never carry secrets, tokens, password hashes or post content.
/// </summary>
[ApiController]
[Route("api/internal")]
[Authorize(Policy = SystemRoles.RequireSystemAdminPolicy)]
public sealed class InternalController(
    AccountService accounts,
    InternalOverviewService overview,
    InternalAdminService admin,
    InternalOperationsService operations,
    PlatformAiConfigurationService ai,
    SocialProviderConfigurationService providers,
    SystemHealthService health,
    ICurrentUser currentUser,
    IHostEnvironment environment) : ControllerBase
{
    [HttpGet("me")]
    public Task<SystemAdminMeDto> Me(CancellationToken cancellationToken) => accounts.GetSystemAdminAsync(currentUser.UserId!.Value, cancellationToken);

    [HttpGet("overview")]
    public Task<InternalOverviewDto> Overview(CancellationToken cancellationToken) => overview.GetAsync(environment.EnvironmentName, cancellationToken);

    // ---- Workspaces ----

    [HttpGet("workspaces")]
    public Task<IReadOnlyList<WorkspaceListItemDto>> Workspaces([FromQuery] string? search, [FromQuery] OrganizationStatus? status,
        [FromQuery] DateTime? createdFrom, CancellationToken cancellationToken) =>
        admin.ListWorkspacesAsync(search, status, Utc(createdFrom), cancellationToken);

    [HttpGet("workspaces/{id:guid}")]
    public Task<WorkspaceDetailDto> Workspace(Guid id, CancellationToken cancellationToken) => admin.GetWorkspaceAsync(id, cancellationToken);

    [HttpPost("workspaces/{id:guid}/suspend")]
    public Task<WorkspaceDetailDto> Suspend(Guid id, CancellationToken cancellationToken) => admin.SuspendWorkspaceAsync(id, cancellationToken);

    [HttpPost("workspaces/{id:guid}/reactivate")]
    public Task<WorkspaceDetailDto> Reactivate(Guid id, CancellationToken cancellationToken) => admin.ReactivateWorkspaceAsync(id, cancellationToken);

    [HttpGet("workspaces/{id:guid}/audit")]
    public Task<IReadOnlyList<AuditLogItemDto>> WorkspaceAudit(Guid id, CancellationToken cancellationToken) => admin.WorkspaceAuditAsync(id, cancellationToken);

    // ---- Users ----

    [HttpGet("users")]
    public Task<IReadOnlyList<UserListItemDto>> Users([FromQuery] string? search, [FromQuery] string? status, [FromQuery] Guid? workspaceId,
        [FromQuery] DateTime? createdFrom, CancellationToken cancellationToken) =>
        admin.ListUsersAsync(search, status switch { "active" => true, "disabled" => false, _ => null }, workspaceId, Utc(createdFrom), cancellationToken);

    [HttpGet("users/{id:guid}")]
    public Task<UserDetailDto> UserDetail(Guid id, CancellationToken cancellationToken) => admin.GetUserAsync(id, cancellationToken);

    [HttpPost("users/{id:guid}/disable")]
    public Task<UserDetailDto> Disable(Guid id, CancellationToken cancellationToken) => admin.DisableUserAsync(id, cancellationToken);

    [HttpPost("users/{id:guid}/enable")]
    public Task<UserDetailDto> Enable(Guid id, CancellationToken cancellationToken) => admin.EnableUserAsync(id, cancellationToken);

    // ---- AI / LLM ----

    [HttpGet("ai/configuration")]
    public Task<AiConfigurationDto> AiConfiguration(CancellationToken cancellationToken) => ai.GetAsync(cancellationToken);

    [HttpPut("ai/configuration")]
    public Task<AiConfigurationDto> SaveAiConfiguration(SaveAiConfigurationRequest request, CancellationToken cancellationToken) =>
        ai.SaveAsync(new SaveAiConfigurationCommand(request.Provider!.Value, request.BaseUrl, request.Model, request.FallbackModel, request.MaxOutputTokens,
            request.RequestTimeoutSeconds, request.Enabled, RowVersion(request.RowVersion)), cancellationToken);

    [HttpPut("ai/api-key")]
    public Task<AiConfigurationDto> ReplaceAiKey(ReplaceSecretRequest request, CancellationToken cancellationToken) =>
        ai.ReplaceApiKeyAsync(request.Value, cancellationToken);

    [EnableRateLimiting(RateLimitingExtensions.Ai)]
    [HttpPost("ai/test")]
    public Task<AiConnectionTestDto> TestAi(TestAiConnectionRequest request, CancellationToken cancellationToken) =>
        ai.TestAsync(request.Provider!.Value, request.BaseUrl, request.Model, request.ApiKey, cancellationToken);

    [HttpPut("ai/image-configuration")]
    public Task<AiConfigurationDto> SaveAiImageConfiguration(SaveAiImageConfigurationRequest request, CancellationToken cancellationToken) =>
        ai.SaveImageAsync(new SaveAiImageConfigurationCommand(request.Provider!.Value, request.BaseUrl, request.Model, request.Size, request.Enabled,
            RowVersion(request.RowVersion)), cancellationToken);

    [HttpPut("ai/image-api-key")]
    public Task<AiConfigurationDto> ReplaceAiImageKey(ReplaceSecretRequest request, CancellationToken cancellationToken) =>
        ai.ReplaceImageApiKeyAsync(request.Value, cancellationToken);

    [EnableRateLimiting(RateLimitingExtensions.Ai)]
    [HttpPost("ai/image-test")]
    public Task<AiConnectionTestDto> TestAiImage(TestAiConnectionRequest request, CancellationToken cancellationToken) =>
        ai.TestImageAsync(request.Provider!.Value, request.BaseUrl, request.Model, request.ApiKey, cancellationToken);

    // ---- Social providers (platform integrations, not customer accounts) ----

    [HttpGet("social-providers")]
    public Task<IReadOnlyList<SocialProviderDto>> SocialProviders(CancellationToken cancellationToken) => providers.ListAsync(cancellationToken);

    [HttpPut("social-providers/linkedin")]
    public Task<SocialProviderDto> SaveLinkedIn(SaveSocialProviderRequest request, CancellationToken cancellationToken) =>
        SaveProvider(PlatformProvider.LINKEDIN, request, cancellationToken);

    [HttpPut("social-providers/meta")]
    public Task<SocialProviderDto> SaveMeta(SaveSocialProviderRequest request, CancellationToken cancellationToken) =>
        SaveProvider(PlatformProvider.META, request, cancellationToken);

    [HttpPut("social-providers/{provider}/secret")]
    public Task<SocialProviderDto> ReplaceProviderSecret(string provider, ReplaceSecretRequest request, CancellationToken cancellationToken) =>
        providers.ReplaceSecretAsync(Provider(provider), request.Value, cancellationToken);

    [HttpPost("social-providers/{provider}/test")]
    public Task<ProviderValidationDto> TestProvider(string provider, CancellationToken cancellationToken) =>
        providers.ValidateAsync(Provider(provider), cancellationToken);

    // ---- Operations ----

    [HttpGet("workers")]
    public Task<IReadOnlyList<WorkerDto>> Workers(CancellationToken cancellationToken) => operations.WorkersAsync(cancellationToken);

    [HttpGet("queues")]
    public async Task<object> Queues(CancellationToken cancellationToken) =>
        new { Ai = await operations.AiQueueAsync(cancellationToken), Publishing = await operations.PublishingQueueAsync(cancellationToken) };

    [HttpGet("queues/ai")]
    public Task<AiQueueViewDto> AiQueue(CancellationToken cancellationToken) => operations.AiQueueAsync(cancellationToken);

    [HttpGet("queues/publishing")]
    public Task<PublishingQueueViewDto> PublishingQueue(CancellationToken cancellationToken) => operations.PublishingQueueAsync(cancellationToken);

    [HttpGet("publications")]
    public Task<IReadOnlyList<PublicationListItemDto>> Publications([FromQuery] Guid? workspaceId, [FromQuery] SocialPlatform? platform,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery(Name = "status")] DeliveryState[]? states, CancellationToken cancellationToken) =>
        operations.PublicationsAsync(new PublicationQueryDto(workspaceId, platform, Utc(from), Utc(to), states), cancellationToken);

    [HttpGet("publications/{id:guid}")]
    public Task<PublicationDetailDto> Publication(Guid id, CancellationToken cancellationToken) => operations.PublicationAsync(id, cancellationToken);

    [HttpGet("reconciliation")]
    public Task<IReadOnlyList<PublicationListItemDto>> Reconciliation(CancellationToken cancellationToken) =>
        operations.PublicationsAsync(new PublicationQueryDto(null, null, null, null, [DeliveryState.RECONCILIATION_REQUIRED]), cancellationToken);

    [HttpGet("reconciliation/{id:guid}")]
    public Task<PublicationDetailDto> ReconciliationItem(Guid id, CancellationToken cancellationToken) => operations.PublicationAsync(id, cancellationToken);

    [HttpPost("reconciliation/{id:guid}/check")]
    public Task<ReconciliationCheckDto> CheckReconciliation(Guid id, CancellationToken cancellationToken) => admin.CheckReconciliationAsync(id, cancellationToken);

    [HttpPost("reconciliation/{id:guid}/confirm-published")]
    public async Task<PublicationDetailDto> ConfirmPublished(Guid id, ConfirmPublishedRequest request, CancellationToken cancellationToken)
    {
        await admin.ConfirmPublishedAsync(id, request.ExternalPostId, request.ExternalPostUrl, cancellationToken);
        return await operations.PublicationAsync(id, cancellationToken);
    }

    [HttpPost("reconciliation/{id:guid}/confirm-failed")]
    public async Task<PublicationDetailDto> ConfirmFailed(Guid id, ConfirmFailedRequest request, CancellationToken cancellationToken)
    {
        await admin.ConfirmFailedAsync(id, request.Reason, cancellationToken);
        return await operations.PublicationAsync(id, cancellationToken);
    }

    // ---- Monitoring ----

    [HttpGet("system-health")]
    public Task<IReadOnlyList<SystemHealthComponentDto>> SystemHealth(CancellationToken cancellationToken) => health.GetAsync(cancellationToken);

    [HttpGet("logs/audit")]
    public Task<IReadOnlyList<AuditLogItemDto>> AuditLog([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? search,
        [FromQuery] string? correlationId, CancellationToken cancellationToken) =>
        operations.AuditLogAsync(Query(from, to, search, correlationId), cancellationToken);

    [HttpGet("logs/signins")]
    public Task<IReadOnlyList<SignInLogItemDto>> SignInLog([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? search,
        [FromQuery] string? correlationId, [FromQuery] string? result, [FromQuery] Guid? userId, [FromQuery] string? ip, CancellationToken cancellationToken) =>
        operations.SignInLogAsync(Query(from, to, search, correlationId), result switch { "success" => true, "failed" => false, _ => null }, userId, ip,
            cancellationToken);

    [HttpGet("logs/ai")]
    public Task<IReadOnlyList<AiLogItemDto>> AiLog([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? search,
        [FromQuery] string? correlationId, CancellationToken cancellationToken) =>
        operations.AiLogAsync(Query(from, to, search, correlationId), cancellationToken);

    [HttpGet("logs/publishing")]
    public Task<IReadOnlyList<PublishingLogItemDto>> PublishingLog([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? search,
        [FromQuery] string? correlationId, CancellationToken cancellationToken) =>
        operations.PublishingLogAsync(Query(from, to, search, correlationId), cancellationToken);

    [HttpGet("logs/system")]
    public Task<IReadOnlyList<SystemLogItemDto>> SystemLog([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? search,
        [FromQuery] string? correlationId, [FromQuery] string? level, [FromQuery] string? service, CancellationToken cancellationToken) =>
        operations.SystemLogAsync(Query(from, to, search, correlationId), level, service, cancellationToken);

    private Task<SocialProviderDto> SaveProvider(PlatformProvider provider, SaveSocialProviderRequest request, CancellationToken cancellationToken) =>
        providers.SaveAsync(new SaveSocialProviderCommand(provider, request.ClientId, request.RedirectUri, null, request.Enabled, RowVersion(request.RowVersion)),
            cancellationToken);

    private static PlatformProvider Provider(string value) => value.ToLowerInvariant() switch
    {
        "linkedin" => PlatformProvider.LINKEDIN,
        "meta" => PlatformProvider.META,
        _ => throw new BusinessException(ErrorCodes.NotFound, "Unknown provider."),
    };

    private static LogQueryDto Query(DateTime? from, DateTime? to, string? search, string? correlationId) =>
        new(Utc(from), Utc(to), string.IsNullOrWhiteSpace(search) ? null : search.Trim(), string.IsNullOrWhiteSpace(correlationId) ? null : correlationId.Trim());

    private static byte[]? RowVersion(string? value) => string.IsNullOrWhiteSpace(value) ? null : Convert.FromBase64String(value);

    private static DateTime? Utc(DateTime? value) =>
        value is { } v ? (v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : DateTime.SpecifyKind(v, DateTimeKind.Utc)) : null;
}
