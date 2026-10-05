using System.Net;
using System.Net.Http.Json;
using SocialMediaAutomation.Application.Authentication.DTOs;
using SocialMediaAutomation.Application.Campaigns.DTOs;
using SocialMediaAutomation.Application.Organizations.DTOs;
using SocialMediaAutomation.Application.SocialAccounts.DTOs;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Infrastructure.Persistence.Seed;
using SocialMediaAutomation.IntegrationTests.Infrastructure;

namespace SocialMediaAutomation.IntegrationTests;

/// <summary>Sign-up through campaign readiness: account and workspace, profile, brand, social accounts, campaign input.</summary>
[Collection(ApiCollection.Name)]
public class RegistrationTests(ApiFactory factory)
{
    private const string Password = "SecurePassword123!";

    private static object Request(string email, string password = Password, string workspaceName = "Centaiva") =>
        new { Email = email, Password = password, WorkspaceName = workspaceName };

    private static string NewEmail() => $"owner-{Guid.NewGuid():N}@example.com";

    [Fact]
    public async Task Register_creates_a_signed_in_account_with_its_workspace()
    {
        var email = NewEmail();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", Request(email));
        var registered = await response.ReadAsync<RegisteredUserDto>();
        var body = await response.Content.ReadAsStringAsync();
        var me = await (await client.GetAsync("/api/v1/me")).ReadAsync<MeDto>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(email, registered.Email);
        Assert.DoesNotContain(Password, body);
        Assert.DoesNotContain("hash", body, StringComparison.OrdinalIgnoreCase);
        var membership = Assert.Single(me.Memberships);
        Assert.Equal((registered.OrganizationId, "Centaiva", OrganizationRole.Admin), (membership.OrganizationId, membership.OrganizationName, membership.Role));
        Assert.Equal((true, false, OnboardingSteps.Profile), (me.Onboarding.WorkspaceCreated, me.Onboarding.ProfileCompleted, me.Onboarding.NextStep));
        await (await client.GetAsync($"/api/v1/organizations/{DemoData.OrganizationAId}")).AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Register_rejects_duplicate_email_short_password_and_bad_input()
    {
        var client = factory.CreateClient();

        await (await client.PostAsJsonAsync("/api/v1/auth/register", Request(DemoData.AdminA.Email.ToUpperInvariant())))
            .AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.EmailAlreadyRegistered);
        await (await client.PostAsJsonAsync("/api/v1/auth/register", Request(NewEmail(), "short")))
            .AssertErrorAsync(HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        await (await client.PostAsJsonAsync("/api/v1/auth/register", Request("not-an-email")))
            .AssertErrorAsync(HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        await (await client.PostAsJsonAsync("/api/v1/auth/register", Request(NewEmail(), workspaceName: " ")))
            .AssertErrorAsync(HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task Login_returns_session_context_and_logout_ends_the_session()
    {
        var email = NewEmail();
        await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/register", Request(email));
        var client = factory.CreateClient();

        var me = await (await client.PostAsJsonAsync("/api/v1/auth/login", new { Email = email.ToUpperInvariant(), Password })).ReadAsync<MeDto>();
        Assert.Equal(("", OnboardingSteps.Profile), (me.DisplayName, me.Onboarding.NextStep));

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/auth/logout", null)).StatusCode);
        await (await client.GetAsync("/api/v1/me")).AssertErrorAsync(HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized);
    }

    [Fact]
    public async Task Invalid_login_gets_one_generic_answer_whether_or_not_the_email_exists()
    {
        var client = factory.CreateClient();

        var wrongPassword = await client.PostAsJsonAsync("/api/v1/auth/login", new { DemoData.CreatorA.Email, Password = "wrong-password" });
        var unknownEmail = await client.PostAsJsonAsync("/api/v1/auth/login", new { Email = NewEmail(), Password = "wrong-password" });

        await wrongPassword.AssertErrorAsync(HttpStatusCode.Unauthorized, ErrorCodes.InvalidCredentials);
        await unknownEmail.AssertErrorAsync(HttpStatusCode.Unauthorized, ErrorCodes.InvalidCredentials);
        Assert.Contains("Invalid email or password.", await wrongPassword.Content.ReadAsStringAsync());
    }

    [Fact]
    [Trait("Category", "Acceptance")]
    public async Task New_user_goes_from_signup_to_a_campaign_ready_for_generation()
    {
        var client = factory.CreateClient();
        // Sign-up: the account, the workspace and the first Admin membership are one transaction.
        var registered = await (await client.PostAsJsonAsync("/api/v1/auth/register", Request(NewEmail()))).ReadAsync<RegisteredUserDto>();
        var orgUrl = $"/api/v1/organizations/{registered.OrganizationId}";

        // Profile: the user's name and the workspace time zone.
        var me = await (await client.PutJsonAsync("/api/v1/me/profile",
            new { registered.OrganizationId, FullName = "Rayyan Khalil", TimeZoneId = "Asia/Karachi" }, ifMatch: null)).ReadAsync<MeDto>();
        var workspace = await (await client.GetAsync(orgUrl)).ReadAsync<OrganizationDto>();
        Assert.Equal(("Rayyan Khalil", "Asia/Karachi", "en", true, OrganizationRole.Admin),
            (me.DisplayName, workspace.TimeZoneId, workspace.DefaultLanguage, workspace.RequireDistinctReviewer, workspace.MyRole));

        var onboarding = await (await client.GetAsync($"/api/v1/onboarding/status?organizationId={workspace.Id}")).ReadAsync<OnboardingStatusDto>();
        Assert.Equal((true, false, false, OnboardingSteps.BrandProfile), (onboarding.WorkspaceCreated, onboarding.BrandProfileCompleted, onboarding.CanCreateCampaign, onboarding.NextStep));
        await (await client.PostAsJsonAsync($"{orgUrl}/campaigns", ApiClient.CampaignBody())).AssertErrorAsync(HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);

        // Brand: four inputs. Campaigns are possible before the social step is finished.
        await (await client.PutJsonAsync($"{orgUrl}/brand-profile", new
        {
            BusinessDescription = "We provide AI-powered reporting and automation software for small businesses.",
            TargetAudience = "Small and medium-sized businesses, operations managers and business owners.",
            BrandTone = "Professional",
            Website = "https://centaiva.com",
        }, ifMatch: null)).ReadAsync<object>();
        onboarding = await (await client.GetAsync("/api/v1/onboarding/status")).ReadAsync<OnboardingStatusDto>();
        Assert.Equal((true, false, true, OnboardingSteps.SocialConnections), (onboarding.BrandProfileCompleted, onboarding.SocialAccountsConnected, onboarding.CanCreateCampaign, onboarding.NextStep));

        // Campaign basics (audience from brand), details, facts, platforms, then the resolved brief.
        var campaign = await client.CreateCampaignAsync(orgUrl, new
        {
            Name = "AI Reporting Dashboard Launch", Objective = "PRODUCT_LAUNCH",
            Description = "We are launching a new AI reporting dashboard that automatically creates weekly business reports.",
        });
        campaign = await (await client.PutJsonAsync($"{orgUrl}/campaigns/{campaign.Id}", new
        {
            campaign.Name, campaign.Objective, campaign.Description, campaign.TargetAudience,
            CallToAction = "BOOK_DEMO", CallToActionUrl = "https://centaiva.com/demo",
        }, ApiClient.ETag(campaign.RowVersion))).ReadAsync<CampaignDto>();
        campaign = await (await client.PutJsonAsync($"{orgUrl}/campaigns/{campaign.Id}/facts",
            new { SupportingFacts = new[] { "Launch date: October 20", "Supports PDF and Excel exports" } }, ApiClient.ETag(campaign.RowVersion))).ReadAsync<CampaignDto>();

        var notYet = await (await client.GetAsync($"{orgUrl}/campaigns/{campaign.Id}/review")).ReadAsync<CampaignBriefDto>();
        Assert.Equal((false, "PLATFORMS"), (notYet.ReadyForGeneration, Assert.Single(notYet.MissingRequirements).Code));

        await (await client.PutJsonAsync($"{orgUrl}/campaigns/{campaign.Id}/platforms",
            new { Platforms = new[] { "LINKEDIN", "FACEBOOK", "INSTAGRAM" } }, ApiClient.ETag(campaign.RowVersion))).ReadAsync<CampaignDto>();
        var brief = await (await client.GetAsync($"{orgUrl}/campaigns/{campaign.Id}/review")).ReadAsync<CampaignBriefDto>();

        Assert.True(brief.ReadyForGeneration);
        Assert.Equal("Small and medium-sized businesses, operations managers and business owners.", brief.Campaign.TargetAudience);
        Assert.Equal(("Centaiva", "", "https://centaiva.com"), (brief.BrandContext!.BusinessName, brief.BrandContext.Industry, brief.BrandContext.Website));
        Assert.Equal(2, brief.SupportingFacts.Count);
        Assert.Equal(CampaignStatus.Draft, brief.Campaign.Status);

        // Social account step: a clearly labelled simulated destination completes onboarding.
        var simulated = await (await client.PostAsJsonAsync($"{orgUrl}/social-accounts/simulated", new { Platform = "LinkedIn" })).ReadAsync<SocialAccountDto>();
        Assert.Equal((ExecutionMode.SIMULATED, "LinkedIn (simulated)"), (simulated.ExecutionMode, simulated.DisplayName));
        var done = await (await client.GetAsync("/api/v1/onboarding/status")).ReadAsync<OnboardingStatusDto>();
        Assert.Equal((true, OnboardingSteps.Complete), (done.SocialAccountsConnected, done.NextStep));
    }

    [Fact]
    public async Task New_user_cannot_reach_another_workspace_by_id()
    {
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/v1/auth/register", Request(NewEmail()));
        var orgA = $"/api/v1/organizations/{DemoData.OrganizationAId}";

        foreach (var url in new[] { orgA, $"{orgA}/brand-profile", $"{orgA}/social-accounts", $"{orgA}/campaigns" })
            await (await client.GetAsync(url)).AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
        await (await client.GetAsync($"/api/v1/onboarding/status?organizationId={DemoData.OrganizationAId}"))
            .AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
        await (await client.PostAsJsonAsync($"{orgA}/social-accounts/simulated", new { Platform = "LinkedIn" }))
            .AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Workspace_requires_industry_and_an_iana_time_zone()
    {
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/v1/auth/register", Request(NewEmail()));

        await (await client.PostAsJsonAsync("/api/v1/organizations", new { Name = "X", Industry = "Retail", TimeZoneId = "UTC+5" }))
            .AssertErrorAsync(HttpStatusCode.BadRequest, ErrorCodes.InvalidTimeZone);
        await (await client.PostAsJsonAsync("/api/v1/organizations", new { Name = "X", TimeZoneId = "Europe/Berlin" }))
            .AssertErrorAsync(HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
    }
}
