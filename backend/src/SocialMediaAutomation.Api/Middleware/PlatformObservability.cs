using Microsoft.AspNetCore.Diagnostics;
using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.Platform;
using SocialMediaAutomation.Domain.Entities;

namespace SocialMediaAutomation.Api.Middleware;

/// <summary>The request's correlation id (set by the request middleware as the trace identifier).</summary>
internal sealed class HttpCorrelationContext(IHttpContextAccessor accessor) : ICorrelationContext
{
    public string? CorrelationId => accessor.HttpContext?.TraceIdentifier;
}

/// <summary>Records unexpected API errors in the System log (redacted), then lets the default 500 response happen.</summary>
internal sealed class UnhandledExceptionRecorder(ISystemEventLog systemLog) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        await systemLog.RecordAsync(SystemEventLevels.Error, "API", "UNHANDLED_EXCEPTION",
            $"{httpContext.Request.Method} {httpContext.Request.Path}: {exception.GetType().Name}: {exception.Message}", httpContext.TraceIdentifier,
            CancellationToken.None);
        return false;
    }
}

internal static class SignInClient
{
    /// <summary>Client hints for the sign-in log: IP (as seen after forwarded-header handling) and a shortened user agent.</summary>
    public static SignInContext From(HttpContext context) =>
        new(context.Connection.RemoteIpAddress?.ToString(), context.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null);
}
