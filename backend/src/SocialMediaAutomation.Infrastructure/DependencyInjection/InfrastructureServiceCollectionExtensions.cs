using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.AiGeneration.Services;
using SocialMediaAutomation.Application.Operations;
using SocialMediaAutomation.Application.Platform;
using SocialMediaAutomation.Infrastructure.Ai;
using SocialMediaAutomation.Infrastructure.Identity;
using SocialMediaAutomation.Infrastructure.OAuth;
using SocialMediaAutomation.Infrastructure.Persistence;
using SocialMediaAutomation.Infrastructure.Persistence.Repositories;
using SocialMediaAutomation.Infrastructure.Persistence.Seed;
using SocialMediaAutomation.Infrastructure.Publishing;
using SocialMediaAutomation.Infrastructure.Security;
using SocialMediaAutomation.Infrastructure.Storage;

namespace SocialMediaAutomation.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    /// <param name="contentRootPath">Base for relative paths in configuration (the host's content root).</param>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, string contentRootPath)
    {
        // Fail at startup rather than on first request; the value comes from user-secrets or environment, never source.
        var connectionString = configuration.GetConnectionString("Core");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Connection string 'ConnectionStrings:Core' is not configured. See README.md.");

        services.TryAddSingleton(TimeProvider.System);
        services.AddDbContext<CoreDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IOrganizationRepository, OrganizationRepository>();
        services.AddScoped<IBrandProfileRepository, BrandProfileRepository>();
        services.AddScoped<ICampaignRepository, CampaignRepository>();
        services.AddScoped<IPostRepository, PostRepository>();
        services.AddScoped<IMediaRepository, MediaRepository>();
        var mediaStorage = CreateMediaStorage(configuration, contentRootPath);
        services.AddSingleton(mediaStorage);
        if (mediaStorage is IStorageProbe probe)
            services.AddSingleton(probe);
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddScoped<DemoDataSeeder>();

        // AI: one OpenAI-compatible client for OpenAI, Groq and custom endpoints. Generous timeout: generation is background work.
        services.AddHttpClient(OpenAiCompatibleClient.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(90));
        services.AddSingleton<ILlmClient, OpenAiCompatibleClient>();
        // Image generation can take a minute or more; it is background work in the AI worker.
        services.AddHttpClient(OpenAiImageClient.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(180));
        services.AddSingleton<OpenAiImageClient>();
        services.AddSingleton<HuggingFaceImageClient>();
        services.AddSingleton<IImageGenerationProvider, ImageGenerationRouter>();
        // The system LLM comes from configuration/environment (Ai__ApiKey etc.); users never configure it.
        var ai = configuration.GetSection(SystemAiOptions.SectionName);
        services.AddSingleton(new SystemAiOptions { Provider = ai["Provider"], BaseUrl = ai["BaseUrl"], Model = ai["Model"], ApiKey = ai["ApiKey"] });
        services.AddScoped<IAiRepository, AiRepository>();
        services.AddScoped<IAiWorkQueue, AiWorkQueue>();
        services.AddScoped<ISocialAccountRepository, SocialAccountRepository>();
        services.AddScoped<IReviewRepository, ReviewRepository>();
        services.AddScoped<IActivityRepository, ActivityRepository>();
        services.AddScoped<IOAuthRepository, OAuthRepository>();
        services.AddScoped<IPublicationRepository, PublicationRepository>();
        services.AddScoped<IPublicationWorkQueue, PublicationWorkQueue>();
        services.AddScoped<IWorkerHeartbeats, WorkerHeartbeats>();
        services.AddScoped<IOperationsRepository, OperationsRepository>();
        services.AddScoped<IPlatformStatsQueries, PlatformStatsQueries>();

        // Publishers: the simulation plus one live adapter per provider. Short timeout: an unanswered publish call is
        // treated as an uncertain outcome and reconciled, never retried blindly.
        services.AddHttpClient(HttpPublisher.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddSingleton<ISocialPublisher, SimulatedPublisher>();
        services.AddScoped<ISocialPublisher>(sp => new LinkedInPublisher(Publisher(sp), configuration));
        services.AddScoped<ISocialPublisher>(sp => new FacebookPublisher(Publisher(sp), configuration));
        services.AddScoped<ISocialPublisher>(sp => new InstagramPublisher(Publisher(sp), configuration));
        services.AddHttpClient(SocialOAuthGateway.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(30));
        // Provider app credentials: platform settings (/internal/social-providers) over configuration, as a refreshed snapshot.
        services.AddSingleton<ProviderAppSource>();
        services.AddSingleton<IProviderAppCache>(sp => sp.GetRequiredService<ProviderAppSource>());
        services.AddSingleton<IProviderAppValidator, ProviderAppValidator>();
        services.AddSingleton<ISocialOAuthGateway, SocialOAuthGateway>();

        // Internal System Admin: platform configuration, secret store, logs and read models.
        services.AddScoped<IPlatformRepository, PlatformRepository>();
        services.AddScoped<ISecretStore, DatabaseSecretStore>();
        services.AddScoped<IInternalQueries, InternalQueries>();
        services.AddSingleton<ISystemEventLog, SystemEventWriter>();

        // Tenant secrets (LLM keys) are encrypted with a key ring shared by the API and the worker.
        // ponytail: key ring on local disk without at-rest key encryption; use a managed store (e.g. Key Vault) when deploying (TASKS.md Phase 29).
        var keysPath = configuration["DataProtection:KeysPath"] is { Length: > 0 } path
            ? Path.Combine(contentRootPath, path)
            : throw new InvalidOperationException("DataProtection:KeysPath is not configured. See README.md.");
        services.AddDataProtection()
            .SetApplicationName("SocialMediaAutomation")
            .PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
        return services;
    }

    private static HttpClient Publisher(IServiceProvider services) =>
        services.GetRequiredService<IHttpClientFactory>().CreateClient(HttpPublisher.HttpClientName);

    private static IMediaStorage CreateMediaStorage(IConfiguration configuration, string contentRootPath)
    {
        var provider = configuration["Storage:Provider"];
        return provider switch
        {
            "Local" => new LocalFileMediaStorage(configuration["Storage:LocalRoot"] is { Length: > 0 } root
                ? Path.Combine(contentRootPath, root)
                : throw new InvalidOperationException("Storage:LocalRoot is required when Storage:Provider is Local.")),
            _ => throw new InvalidOperationException($"Storage:Provider '{provider}' is not supported. Use Local (see README.md)."),
        };
    }
}
