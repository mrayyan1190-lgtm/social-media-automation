using Microsoft.Extensions.DependencyInjection;
using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.Authentication.Services;
using SocialMediaAutomation.Application.Authentication.DTOs;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Infrastructure.Persistence.Seed;
using SocialMediaAutomation.IntegrationTests.Infrastructure;

namespace SocialMediaAutomation.IntegrationTests;

/// <summary>The account rules behind "Continue with Google" (the Google redirect itself is the framework's handler).</summary>
[Collection(ApiCollection.Name)]
public class GoogleSignInTests(ApiFactory factory)
{
    private async Task<(Guid? UserId, Guid? KnownUserId, string? Failure)> SignInAsync(string? email, bool verified)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AccountService>().SignInWithGoogleAsync(email, verified, CancellationToken.None);
    }

    [Fact]
    public async Task Unverified_or_missing_email_never_signs_in()
    {
        Assert.Equal(SignInEvents.InvalidCredentials, (await SignInAsync(DemoData.AdminA.Email, verified: false)).Failure);
        Assert.Null((await SignInAsync(null, verified: true)).UserId);
    }

    [Fact]
    public async Task Verified_email_signs_in_the_existing_account()
    {
        var result = await SignInAsync(DemoData.AdminA.Email.ToUpperInvariant(), verified: true);

        Assert.Equal(DemoData.AdminA.Id, result.UserId);
    }

    [Fact]
    public async Task New_verified_email_gets_one_account_that_onboarding_asks_for_a_workspace()
    {
        var email = $"google-{Guid.NewGuid():N}@example.com";

        var first = await SignInAsync(email, verified: true);
        var second = await SignInAsync(email, verified: true);

        Assert.NotNull(first.UserId);
        Assert.Equal(first.UserId, second.UserId);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<IOrganizationRepository>()
            .ListActiveMembershipsForUserAsync(first.UserId!.Value, CancellationToken.None));
        var me = await scope.ServiceProvider.GetRequiredService<AccountService>().GetMeAsync(first.UserId.Value, CancellationToken.None);
        Assert.Equal((email, OnboardingSteps.Workspace), (me.Email, me.Onboarding.NextStep));
    }
}
