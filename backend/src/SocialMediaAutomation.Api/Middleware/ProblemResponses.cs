using Microsoft.AspNetCore.Mvc;
using SocialMediaAutomation.Domain.Constants;

namespace SocialMediaAutomation.Api.Middleware;

/// <summary>Maps stable error codes to HTTP statuses and writes RFC 7807 problem responses carrying the code.</summary>
internal static class ProblemResponses
{
    public static int StatusFor(string code) => code switch
    {
        ErrorCodes.Unauthorized or ErrorCodes.InvalidCredentials => StatusCodes.Status401Unauthorized,
        ErrorCodes.Forbidden or ErrorCodes.DistinctReviewerRequired or ErrorCodes.WorkspaceSuspended or ErrorCodes.AccountDisabled => StatusCodes.Status403Forbidden,
        ErrorCodes.NotFound or ErrorCodes.TenantMismatch => StatusCodes.Status404NotFound,
        ErrorCodes.ConcurrencyConflict or ErrorCodes.InvalidStateTransition or ErrorCodes.AlreadyExists or ErrorCodes.AiNotConfigured
            or ErrorCodes.SubmissionBlocked or ErrorCodes.ApprovalStale or ErrorCodes.ProviderNotConfigured
            or ErrorCodes.SocialAccountNotReady or ErrorCodes.ApprovalRequired or ErrorCodes.IdempotencyConflict
            or ErrorCodes.PublicationTooLateToCancel or ErrorCodes.EmailAlreadyRegistered
            or ErrorCodes.CampaignNotReadyForGeneration => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest,
    };

    /// <summary>
    /// Model-binding failures for fields with a dedicated code (an unknown objective or platform) get that code,
    /// so the frontend never parses messages; anything else is VALIDATION_FAILED.
    /// </summary>
    public static string CodeForInvalidFields(IEnumerable<string> fieldKeys)
    {
        var keys = fieldKeys.ToList();
        if (keys.Any(k => k.Contains("objective", StringComparison.OrdinalIgnoreCase)))
            return ErrorCodes.InvalidCampaignObjective;
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

    public static Task WriteAsync(HttpContext context, string code, string detail)
    {
        var problem = Create(code, detail);
        problem.Extensions["correlationId"] = context.TraceIdentifier;
        context.Response.StatusCode = problem.Status!.Value;
        return context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json");
    }
}
