using SocialMediaAutomation.Infrastructure.Persistence.Seed;

namespace SocialMediaAutomation.Api.Extensions;

internal static class DemoSeedExtensions
{
    /// <summary>
    /// Seeds demo data when "Seed:DemoData" is true (Development default). Passwords come only from configuration, never source:
    /// "Seed:DemoPassword" (demo organizations A/B), "Seed:DemoWorkspacePassword" (Centaiva Demo Workspace) and, in the
    /// Development environment only, "Seed:SystemAdmin:Email/Password" (internal system administrator).
    /// Each part is skipped when its password is not configured.
    /// </summary>
    public static async Task SeedDemoDataIfEnabledAsync(this WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>("Seed:DemoData"))
            return;

        await using var scope = app.Services.CreateAsyncScope();
        var seeder = scope.ServiceProvider.GetRequiredService<DemoDataSeeder>();

        var password = app.Configuration["Seed:DemoPassword"];
        if (string.IsNullOrWhiteSpace(password))
            app.Logger.LogWarning("Seed:DemoData is enabled but Seed:DemoPassword is not set; demo organizations A/B were not seeded. See README.md.");
        else
            app.Logger.LogInformation(await seeder.SeedAsync(password, CancellationToken.None)
                ? "Demo organizations and users seeded" : "Demo data already present; seeding skipped");

        var workspacePassword = app.Configuration["Seed:DemoWorkspacePassword"];
        if (!string.IsNullOrWhiteSpace(workspacePassword) && await seeder.SeedCentaivaAsync(workspacePassword, CancellationToken.None))
            app.Logger.LogInformation("Centaiva Demo Workspace seeded");

        var adminEmail = app.Configuration["Seed:SystemAdmin:Email"];
        var adminPassword = app.Configuration["Seed:SystemAdmin:Password"];
        if (app.Environment.IsDevelopment() && !string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminPassword)
            && await seeder.SeedSystemAdminAsync(adminEmail, adminPassword, app.Configuration["Seed:SystemAdmin:DisplayName"] ?? "System Administrator", CancellationToken.None))
            app.Logger.LogInformation("Development system administrator seeded");
    }
}
