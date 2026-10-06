using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using SocialMediaAutomation.Domain.Constants;

namespace SocialMediaAutomation.Api.Middleware;

/// <summary>
/// Last exception handler: every failure still answers with a stable code. Internal details (exception type, message,
/// stack, SQL) never reach the client; the correlation id in the response links to the server log.
/// </summary>
internal sealed class UnhandledExceptionHandler(ILogger<UnhandledExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (httpContext.Response.HasStarted)
            return false;

        // The browser went away; nobody is waiting for an answer.
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            httpContext.Response.StatusCode = 499;
            return true;
        }

        // Two saves raced on the same row: the same answer as a stale If-Match.
        if (exception is DbUpdateConcurrencyException)
        {
            await ProblemResponses.WriteAsync(httpContext, ErrorCodes.ConcurrencyConflict,
                "This item was changed by someone else. Reload it and try again.");
            return true;
        }

        logger.LogError(exception, "Unhandled exception for {Method} {Path} ({CorrelationId})",
            httpContext.Request.Method, httpContext.Request.Path, httpContext.TraceIdentifier);
        await ProblemResponses.WriteAsync(httpContext, ErrorCodes.InternalError,
            "Something went wrong on our side. Please try again in a moment.");
        return true;
    }
}
