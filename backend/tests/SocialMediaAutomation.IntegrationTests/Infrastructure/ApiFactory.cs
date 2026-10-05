using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SocialMediaAutomation.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using SocialMediaAutomation.Application.AiGeneration.Services;
using SocialMediaAutomation.Application.Publications.Services;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Infrastructure.Publishing;
using SocialMediaAutomation.Infrastructure.Persistence;
using SocialMediaAutomation.Infrastructure.Persistence.Seed;

namespace SocialMediaAutomation.IntegrationTests.Infrastructure;

/// <summary>
/// Runs the real API against a throwaway SQL Server database: applies migrations, seeds demo data through the
/// normal startup path, and drops the database afterwards. The server comes from the developer's
/// ConnectionStrings:Core (user-secrets) or the ConnectionStrings__Core environment variable.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string connectionString = CreateTestConnectionString();

    public string DemoPassword { get; } = $"Test-{Guid.NewGuid():N}!Aa1";

    /// <summary>Throwaway object storage for this run; deleted on dispose.</summary>
    public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), $"sma-test-media-{Guid.NewGuid():N}");

    /// <summary>Everything the API host logged during the run (for "no secrets in logs" checks).</summary>
    public CapturingLoggerProvider Logs { get; } = new();

    /// <summary>Scripted live-provider behaviour for publishing tests.</summary>
    public PublisherScript Publishing { get; } = new();

    /// <summary>Replaces the hosted LLM for the whole run; tests script it per scenario.</summary>
    public FakeLlmClient Llm { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Core", connectionString);
        builder.UseSetting("Seed:DemoData", "true");
        builder.UseSetting("Seed:DemoPassword", DemoPassword);
        builder.UseSetting("Storage:Provider", "Local");
        builder.UseSetting("Storage:LocalRoot", StorageRoot);
        builder.UseSetting("DataProtection:KeysPath", Path.Combine(StorageRoot, "keys"));
        builder.UseSetting("App:WebBaseUrl", "http://web.test");
        // Generous limits so the shared test host is never throttled; HardeningTests lowers them on a derived host.
        foreach (var policy in new[] { "Login", "Ai", "OAuth", "Upload" })
            builder.UseSetting($"RateLimiting:{policy}PerMinute", "100000");
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ILlmClient>(Llm);
            services.AddSingleton<ISocialOAuthGateway>(new FakeOAuthGateway());
            // Real simulation; scripted stand-ins for the live provider adapters (no provider is ever called in tests).
            services.RemoveAll<ISocialPublisher>();
            services.AddSingleton<ISocialPublisher, SimulatedPublisher>();
            services.AddSingleton<ISocialPublisher>(new FakeLivePublisher(Publishing, false, SocialPlatform.LinkedIn, SocialPlatform.Facebook));
            services.AddSingleton<ISocialPublisher>(new FakeLivePublisher(Publishing, true, SocialPlatform.Instagram));
        });
    }

    public async Task InitializeAsync()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
        _ = Server; // starts the host, which runs the demo seed
        // The existing operations/AI-settings tests act as system admins; in the product this flag is never set through the API.
        Guid[] systemAdmins = [DemoData.AdminA.Id, DemoData.AdminB.Id];
        await db.Users.Where(u => systemAdmins.Contains(u.Id)).ExecuteUpdateAsync(u => u.SetProperty(x => x.IsSystemAdmin, true));
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await using (var db = CreateDbContext())
            await db.Database.EnsureDeletedAsync();
        await base.DisposeAsync();
        if (Directory.Exists(StorageRoot))
            Directory.Delete(StorageRoot, recursive: true);
    }

    /// <summary>Runs the AI worker pipeline in-process until no due request remains (the real worker host is not started in tests).</summary>
    public async Task<int> DrainAiQueueAsync()
    {
        var processed = 0;
        while (true)
        {
            await using var scope = Services.CreateAsyncScope();
            if (!await scope.ServiceProvider.GetRequiredService<AiGenerationProcessor>().ProcessNextAsync("test-worker", CancellationToken.None))
                return processed;
            processed++;
        }
    }

    /// <summary>
    /// Runs the publishing worker pipeline in-process until nothing is due: sweep abandoned leases, claim a batch, process
    /// each job in its own scope (the real worker host is not started in tests).
    /// </summary>
    public async Task<int> DrainPublicationsAsync(string workerId = "test-worker")
    {
        var processed = 0;
        for (var pass = 0; pass < 50; pass++)
        {
            IReadOnlyList<(Guid JobId, Guid LeaseToken)> claims;
            await using (var scope = Services.CreateAsyncScope())
            {
                var processor = scope.ServiceProvider.GetRequiredService<PublicationProcessor>();
                await processor.SweepAbandonedAsync(CancellationToken.None);
                claims = await processor.ClaimAsync(workerId, CancellationToken.None);
            }
            if (claims.Count == 0)
                return processed;
            foreach (var (jobId, leaseToken) in claims)
                await ProcessPublicationAsync(jobId, leaseToken, workerId);
            processed += claims.Count;
        }
        throw new InvalidOperationException("Publication jobs kept becoming due; a test is looping.");
    }

    public async Task ProcessPublicationAsync(Guid jobId, Guid leaseToken, string workerId = "test-worker")
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PublicationProcessor>().ProcessAsync(jobId, leaseToken, workerId, CancellationToken.None);
    }

    public async Task<IReadOnlyList<(Guid JobId, Guid LeaseToken)>> ClaimPublicationsAsync(string workerId)
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PublicationProcessor>().ClaimAsync(workerId, CancellationToken.None);
    }

    public async Task SweepPublicationsAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PublicationProcessor>().SweepAbandonedAsync(CancellationToken.None);
    }

    public CoreDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<CoreDbContext>().UseSqlServer(connectionString).Options);

    private static string CreateTestConnectionString()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets("social-media-automation-dev")
            .AddEnvironmentVariables()
            .Build();

        var baseConnectionString = configuration.GetConnectionString("Core");
        if (string.IsNullOrWhiteSpace(baseConnectionString))
            throw new InvalidOperationException(
                "Integration tests need SQL Server. Set ConnectionStrings:Core via user-secrets or ConnectionStrings__Core (README.md §13).");

        return new SqlConnectionStringBuilder(baseConnectionString)
        {
            InitialCatalog = $"SocialMediaAutomation_Test_{Guid.NewGuid():N}",
        }.ConnectionString;
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
