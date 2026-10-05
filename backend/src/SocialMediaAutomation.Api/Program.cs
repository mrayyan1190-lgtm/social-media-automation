using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using SocialMediaAutomation.Api.Extensions;
using SocialMediaAutomation.Api.Middleware;
using SocialMediaAutomation.Application.Common;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Infrastructure.DependencyInjection;
using SocialMediaAutomation.Infrastructure.Persistence;
using SocialMediaAutomation.Infrastructure.Persistence.Repositories;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment.ContentRootPath);
builder.Services.AddSessionAuthentication(builder.Environment);
builder.Services.AddApiRateLimiting(builder.Configuration);
// TLS terminates at the host or a reverse proxy; trust X-Forwarded-* only when configured for that deployment.
var trustForwardedHeaders = builder.Configuration.GetValue("Security:TrustForwardedHeaders", false);
var requireHttps = builder.Configuration.GetValue("Security:RequireHttps", builder.Environment.IsProduction());
if (trustForwardedHeaders)
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<BusinessExceptionHandler>();
builder.Services.AddExceptionHandler<UnhandledExceptionRecorder>();
builder.Services.AddScoped<SocialMediaAutomation.Application.Abstractions.ICorrelationContext, HttpCorrelationContext>();
builder.Services
    .AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)))
    .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = context =>
    {
        var code = ProblemResponses.CodeForInvalidFields(context.ModelState.Where(e => e.Value!.Errors.Count > 0).Select(e => e.Key));
        var problem = new ValidationProblemDetails(context.ModelState) { Status = StatusCodes.Status400BadRequest, Title = code };
        problem.Extensions["code"] = code;
        problem.Extensions["correlationId"] = context.HttpContext.TraceIdentifier;
        return new BadRequestObjectResult(problem);
    });
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"])
    .AddCheck<WorkerHeartbeatHealthCheck>("workers", tags: ["ready"]);

var app = builder.Build();

if (trustForwardedHeaders)
    app.UseForwardedHeaders();
if (requireHttps)
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseRequestHardening();
app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
// Liveness: the process answers. Readiness: database reachable; workers report Degraded (not Unhealthy) when silent.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();

await app.SeedDemoDataIfEnabledAsync();
// Load the social provider settings saved in /internal/social-providers (falls back to configuration when unavailable).
await app.Services.GetRequiredService<SocialMediaAutomation.Application.Abstractions.IProviderAppCache>().RefreshAsync(CancellationToken.None);
await app.Services.GetRequiredService<SocialMediaAutomation.Application.Abstractions.ISystemEventLog>()
    .RecordAsync(SocialMediaAutomation.Domain.Entities.SystemEventLevels.Info, "API", "SERVICE_STARTED", $"API started ({app.Environment.EnvironmentName}).");

app.Run();

public partial class Program;
