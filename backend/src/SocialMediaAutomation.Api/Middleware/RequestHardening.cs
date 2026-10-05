using System.Diagnostics;
using System.Text.RegularExpressions;
using SocialMediaAutomation.Domain.Constants;

namespace SocialMediaAutomation.Api.Middleware;

/// <summary>
/// Cross-cutting request handling (TASKS.md Phases 26 and 28): a correlation id on every request, log line and problem
/// response; security headers; and an Origin check on state-changing requests as anti-forgery in addition to the
/// SameSite=Strict session cookie.
/// </summary>
internal static partial class RequestHardening
{
    public const string CorrelationHeader = "X-Correlation-Id";

    [GeneratedRegex("^[A-Za-z0-9._-]{8,64}$")]
    private static partial Regex SafeCorrelationId();

    public static IApplicationBuilder UseRequestHardening(this WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("SocialMediaAutomation.Api.Requests");
        var allowedOrigins = AllowedOrigins(app.Configuration);

        return app.Use(async (context, next) =>
        {
            // Accept a caller's id only if it is harmless to echo and log; otherwise mint one.
            var incoming = context.Request.Headers[CorrelationHeader].ToString();
            var correlationId = SafeCorrelationId().IsMatch(incoming) ? incoming : Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
            context.TraceIdentifier = correlationId;

            // Applied when the response starts: the exception handler clears headers set earlier, and error responses
            // need them as much as successful ones.
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers[CorrelationHeader] = correlationId;
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY";
                headers["Referrer-Policy"] = "no-referrer";
                headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
                if (context.Request.Path.StartsWithSegments("/api") && string.IsNullOrEmpty(headers.CacheControl))
                    headers.CacheControl = "no-store";
                return Task.CompletedTask;
            });

            using var scope = logger.BeginScope("CorrelationId:{CorrelationId}", correlationId);
            var started = Stopwatch.GetTimestamp();

            if (IsUnsafe(context.Request.Method) && !OriginAllowed(context.Request, allowedOrigins))
            {
                logger.LogWarning("Rejected {Method} {Path}: cross-site origin {Origin}", context.Request.Method, context.Request.Path, context.Request.Headers.Origin.ToString());
                await ProblemResponses.WriteAsync(context, ErrorCodes.Forbidden, "Cross-site requests are not allowed.");
                return;
            }

            try
            {
                await next(context);
            }
            finally
            {
                // Path only: query strings can carry OAuth codes and states, which never belong in logs.
                logger.LogInformation("{Method} {Path} -> {StatusCode} in {ElapsedMs:0} ms", context.Request.Method, context.Request.Path,
                    context.Response.StatusCode, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
        });
    }

    private static bool IsUnsafe(string method) => !(HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method));

    /// <summary>
    /// Browsers send Origin on cross-site and on same-origin unsafe requests; a request without one does not come from
    /// a cross-site page. The API's own origin and the web app's origin (App:WebBaseUrl, e.g. the dev proxy) are allowed.
    /// </summary>
    private static bool OriginAllowed(HttpRequest request, IReadOnlySet<string> allowed)
    {
        var origin = request.Headers.Origin.ToString();
        if (origin.Length == 0)
            return true;
        return allowed.Contains(origin.TrimEnd('/')) || string.Equals(origin.TrimEnd('/'), $"{request.Scheme}://{request.Host}", StringComparison.OrdinalIgnoreCase);
    }

    private static HashSet<string> AllowedOrigins(IConfiguration configuration) =>
        (configuration["App:WebBaseUrl"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(o => o.TrimEnd('/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
