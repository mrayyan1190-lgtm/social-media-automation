using Microsoft.AspNetCore.Diagnostics;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Api.Middleware;

/// <summary>Turns <see cref="BusinessException"/> into a problem response; anything else falls through to the default 500 handler.</summary>
internal sealed class BusinessExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not BusinessException business)
            return false;

        await ProblemResponses.WriteAsync(httpContext, business.Code, business.Message);
        return true;
    }
}
