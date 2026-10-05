using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using SocialMediaAutomation.Api.Middleware;

namespace SocialMediaAutomation.Api.Extensions;

/// <summary>
/// Per-minute limits for sensitive or costly endpoints (TASKS.md Phase 26): sign-in per client address, and AI calls,
/// provider connection starts and uploads per user. Limits come from "RateLimiting:{Policy}PerMinute".
/// </summary>
internal static class RateLimitingExtensions
{
    public const string Login = "login";
    public const string Ai = "ai";
    public const string OAuth = "oauth";
    public const string Upload = "upload";
    public const string RateLimitedCode = "RATE_LIMITED";

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        int Limit(string policy, int fallback) => configuration.GetValue($"RateLimiting:{policy}PerMinute", fallback);

        return services.AddRateLimiter(options =>
        {
            options.AddPolicy(Login, context => Window($"login:{context.Connection.RemoteIpAddress}", Limit("Login", 10)));
            options.AddPolicy(Ai, context => Window($"ai:{User(context)}", Limit("Ai", 20)));
            options.AddPolicy(OAuth, context => Window($"oauth:{User(context)}", Limit("OAuth", 10)));
            options.AddPolicy(Upload, context => Window($"upload:{User(context)}", Limit("Upload", 30)));
            options.OnRejected = async (rejected, _) =>
            {
                if (rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    rejected.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
                rejected.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                var problem = ProblemResponses.Create(RateLimitedCode, "Too many requests. Wait a moment and try again.");
                problem.Status = StatusCodes.Status429TooManyRequests;
                problem.Extensions["correlationId"] = rejected.HttpContext.TraceIdentifier;
                await rejected.HttpContext.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json");
            };
        });
    }

    private static string User(HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";

    private static RateLimitPartition<string> Window(string key, int permitsPerMinute) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, permitsPerMinute),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
}
