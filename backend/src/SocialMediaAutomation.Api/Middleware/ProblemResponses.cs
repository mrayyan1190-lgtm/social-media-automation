using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.Platform;
using SocialMediaAutomation.Domain.Common;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Entities;

namespace SocialMediaAutomation.Api.Middleware;

/// <summary>Maps stable error codes to HTTP statuses and writes RFC 7807 problem responses carrying the code.</summary>
internal static class ProblemResponses
{
    public static int StatusFor(string code) => code switch
    {
        ErrorCodes.Unauthorized or ErrorCodes.InvalidCredentials => StatusCodes.Status401Unauthorized,
        ErrorCodes.Forbidden or ErrorCodes.DistinctReviewerRequired or ErrorCodes.WorkspaceSuspended or ErrorCodes.AccountDisabled => StatusCodes.Status403Forbidden,
        ErrorCodes.NotFound or ErrorCodes.TenantMismatch => StatusCodes.Status404NotFound,
        ErrorCodes.InternalError => StatusCodes.Status500InternalServerError,
        ErrorCodes.ConcurrencyConflict or ErrorCodes.InvalidStateTransition or ErrorCodes.AlreadyExists or ErrorCodes.AiNotConfigured
            or ErrorCodes.SubmissionBlocked or ErrorCodes.ApprovalStale or ErrorCodes.ProviderNotConfigured
            or ErrorCodes.SocialAccountNotReady or ErrorCodes.ApprovalRequired or ErrorCodes.IdempotencyConflict
            or ErrorCodes.PublicationTooLateToCancel or ErrorCodes.EmailAlreadyRegistered => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest,
    };

    /// <summary>
    /// Model-binding failures for fields with a dedicated code (an unknown objective or platform) get that code,
    /// so the frontend never parses messages; anything else is VALIDATION_FAILED.
    /// </summary>
    public static string CodeForInvalidFields(IEnumerable<string> fieldKeys)
    {
        var keys = fieldKeys.ToList();
        if (keys.Any(k => k.Contains("platform", StringComparison.OrdinalIgnoreCase)))
            return ErrorCodes.InvalidPlatform;
        return ErrorCodes.ValidationFailed;
    }

    public static ProblemDetails Create(string code, string detail)
    {
        var problem = new ProblemDetails { Status = StatusFor(code), Title = code, Detail = detail };
        problem.Extensions["code"] = code;
        return problem;
    }

    public static async Task WriteAsync(HttpContext context, string code, string detail, int? status = null)
    {
        var problem = Create(code, detail);
        if (status is { } explicitStatus)
            problem.Status = explicitStatus;
        problem.Extensions["correlationId"] = context.TraceIdentifier;
        context.Response.StatusCode = problem.Status!.Value;
        await context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json");
        await RecordFailureAsync(context, problem.Status.Value, code, detail);
    }

    /// <summary>
    /// Every failed API request goes to the System log (Monitoring → Logs): method, path, status, code, user and the
    /// correlation id, redacted. Written in its own scope (<see cref="ISystemEventLog"/>), so it never affects the response.
    /// </summary>
    public static Task RecordFailureAsync(HttpContext context, int status, string code, string detail)
    {
        // Not signed in is the normal state of a signed-out browser checking its session; it is not an error.
        if (status == StatusCodes.Status401Unauthorized && code == ErrorCodes.Unauthorized)
            return Task.CompletedTask;
        // Unexpected exceptions are already recorded, with the exception details, as UNHANDLED_EXCEPTION.
        if (code == ErrorCodes.InternalError)
            return Task.CompletedTask;
        var user = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var message = SecretRedactor.Redact(
            $"{context.Request.Method} {context.Request.Path} → {status} {code}: {detail}{(user is null ? "" : $" (user {user})")}");
        return context.RequestServices.GetRequiredService<ISystemEventLog>().RecordAsync(
            status >= 500 ? SystemEventLevels.Error : SystemEventLevels.Warning, "API", "REQUEST_FAILED", message, context.TraceIdentifier,
            CancellationToken.None);
    }
}
