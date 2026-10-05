using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.Common;
using SocialMediaAutomation.Infrastructure.DependencyInjection;
using SocialMediaAutomation.Worker.Extensions;
using SocialMediaAutomation.Worker.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment.ContentRootPath);
builder.Services.AddSingleton<ICurrentUser, NoUserContext>();
builder.Services.AddSingleton<ICorrelationContext, NoCorrelationContext>();
var mode = builder.Services.AddWorkers(builder.Configuration);

var host = builder.Build();
// Provider app settings from /internal/social-providers, and a start event for the System log.
await host.Services.GetRequiredService<IProviderAppCache>().RefreshAsync(CancellationToken.None);
await host.Services.GetRequiredService<ISystemEventLog>().RecordAsync(SocialMediaAutomation.Domain.Entities.SystemEventLevels.Info,
    mode == WorkerMode.Publishing ? "Publishing Worker" : "AI Worker",
    "SERVICE_STARTED", $"Worker started in {mode} mode.");
host.Services.GetRequiredService<ILogger<Program>>().LogInformation("Worker starting in {WorkerMode} mode", mode);
host.Run();
