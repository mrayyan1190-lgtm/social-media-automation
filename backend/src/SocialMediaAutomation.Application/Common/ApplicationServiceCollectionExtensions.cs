using SocialMediaAutomation.Application.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SocialMediaAutomation.Application.AiGeneration.Services;
using SocialMediaAutomation.Application.Audit.Services;
using SocialMediaAutomation.Application.Authentication.Services;
using SocialMediaAutomation.Application.Notifications.Services;
using SocialMediaAutomation.Application.Reviews.Services;
using SocialMediaAutomation.Application.SocialAccounts.Services;
using SocialMediaAutomation.Application.BrandProfiles.Services;
using SocialMediaAutomation.Application.Media.Services;
using SocialMediaAutomation.Application.Posts.Services;
using SocialMediaAutomation.Application.Common.Authorization;
using SocialMediaAutomation.Application.Organizations.Services;

using SocialMediaAutomation.Application.Publications.Services;
using SocialMediaAutomation.Application.Dashboard;
using SocialMediaAutomation.Application.Operations;

namespace SocialMediaAutomation.Application.Common;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<OrganizationAuthorizer>();
        services.AddScoped<SystemAdminAuthorizer>();
        services.AddScoped<AccountService>();
        services.AddScoped<OrganizationService>();
        services.AddScoped<OrganizationMemberService>();
        services.AddScoped<BrandProfileService>();
        services.AddScoped<MediaService>();
        services.AddScoped<PostService>();
        services.AddScoped<AiSettingsService>();
        services.AddScoped<AiConnectionResolver>();
        services.AddScoped<AiGenerationService>();
        services.AddScoped<AiGenerationProcessor>();
        services.AddScoped<ActivityRecorder>();
        services.AddScoped<SocialAccountService>();
        services.AddScoped<SocialConnectionService>();
        services.AddScoped<ReviewService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<AuditService>();
        services.AddScoped<PublicationService>();
        services.AddScoped<PublicationProcessor>();
        services.AddScoped<DashboardService>();
        services.AddScoped<OperationsService>();
        services.AddScoped<InternalOverviewService>();
        services.AddScoped<PlatformAudit>();
        services.AddScoped<SignInRecorder>();
        services.AddScoped<PlatformAiConfigurationService>();
        services.AddScoped<SocialProviderConfigurationService>();
        services.AddScoped<InternalAdminService>();
        services.AddScoped<InternalOperationsService>();
        services.AddScoped<SystemHealthService>();
        return services;
    }
}
