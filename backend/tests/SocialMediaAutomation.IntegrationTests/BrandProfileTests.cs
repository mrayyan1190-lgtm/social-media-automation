using System.Net;
using SocialMediaAutomation.Application.BrandProfiles.DTOs;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Infrastructure.Persistence.Seed;
using SocialMediaAutomation.IntegrationTests.Infrastructure;

namespace SocialMediaAutomation.IntegrationTests;

[Collection(ApiCollection.Name)]
public class BrandProfileTests(ApiFactory factory)
{
    private static readonly string BrandA = $"/api/v1/organizations/{DemoData.OrganizationAId}/brand-profile";

    [Fact]
    public async Task Admin_creates_profile_in_new_workspace_and_it_reloads()
    {
        var (admin, brandUrl) = await NewWorkspaceAsync();

        await (await admin.GetAsync(brandUrl)).AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
        var saved = await (await admin.PutJsonAsync(brandUrl, ApiClient.BrandBody(), ifMatch: null)).ReadAsync<BrandProfileDto>();
        var reloaded = await (await admin.GetAsync(brandUrl)).ReadAsync<BrandProfileDto>();

        Assert.Equal(("We sell outdoor gear.", "Weekend hikers", BrandTone.Friendly, "https://acme.example"),
            (reloaded.BusinessDescription, reloaded.TargetAudience, reloaded.BrandTone, reloaded.Website));
        Assert.Equal(["guaranteed"], reloaded.ProhibitedTerms);
        Assert.Equal(saved.RowVersion, reloaded.RowVersion);
    }

    [Fact]
    public async Task Website_and_prohibited_terms_are_optional()
    {
        var (admin, brandUrl) = await NewWorkspaceAsync();

        var saved = await (await admin.PutJsonAsync(brandUrl,
            new { BusinessDescription = "We sell outdoor gear.", TargetAudience = "Hikers", BrandTone = "Casual" }, ifMatch: null)).ReadAsync<BrandProfileDto>();

        Assert.Null(saved.Website);
        Assert.Empty(saved.ProhibitedTerms);
    }

    [Fact]
    public async Task Update_requires_current_etag_and_stale_update_conflicts()
    {
        var (admin, brandUrl) = await NewWorkspaceAsync();
        var created = await (await admin.PutJsonAsync(brandUrl, ApiClient.BrandBody(), ifMatch: null)).ReadAsync<BrandProfileDto>();

        var missingIfMatch = await admin.PutJsonAsync(brandUrl, ApiClient.BrandBody("Second"), ifMatch: null);
        var updated = await (await admin.PutJsonAsync(brandUrl, ApiClient.BrandBody("Second"), ApiClient.ETag(created.RowVersion))).ReadAsync<BrandProfileDto>();
        var stale = await admin.PutJsonAsync(brandUrl, ApiClient.BrandBody("Lost update"), ApiClient.ETag(created.RowVersion));

        await missingIfMatch.AssertErrorAsync(HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        Assert.Equal("Second", updated.BusinessDescription);
        await stale.AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.ConcurrencyConflict);
    }

    [Fact]
    public async Task Other_workspace_cannot_read_or_modify_profile()
    {
        var adminA = await factory.SignInAsync(DemoData.AdminA);
        var adminB = await factory.SignInAsync(DemoData.AdminB);
        var before = await (await adminA.GetAsync(BrandA)).ReadAsync<BrandProfileDto>();

        await (await adminB.GetAsync(BrandA)).AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
        await (await adminB.PutJsonAsync(BrandA, ApiClient.BrandBody("Hijacked"), ApiClient.ETag(before.RowVersion)))
            .AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);

        var after = await (await adminA.GetAsync(BrandA)).ReadAsync<BrandProfileDto>();
        Assert.Equal(before.BusinessDescription, after.BusinessDescription);
        Assert.Equal(before.RowVersion, after.RowVersion);
    }

    [Fact]
    public async Task Creator_and_reviewer_can_read_but_not_change_brand()
    {
        foreach (var user in new[] { DemoData.CreatorA, DemoData.ReviewerA })
        {
            var client = await factory.SignInAsync(user);
            var profile = await (await client.GetAsync(BrandA)).ReadAsync<BrandProfileDto>();

            await (await client.PutJsonAsync(BrandA, ApiClient.BrandBody(), ApiClient.ETag(profile.RowVersion)))
                .AssertErrorAsync(HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        }
    }

    [Theory]
    [InlineData("Shouty", "https://acme.example")] // unknown tone
    [InlineData("Friendly", "acme.example")] // not an absolute URL
    public async Task Invalid_tone_or_website_is_rejected(string tone, string website)
    {
        var (admin, brandUrl) = await NewWorkspaceAsync();

        var response = await admin.PutJsonAsync(brandUrl, ApiClient.BrandBody(tone: tone, website: website), ifMatch: null);

        await response.AssertErrorAsync(HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
    }

    /// <summary>A fresh workspace owned by Org B's admin, so profile creation is exercised without touching seeded data.</summary>
    private async Task<(HttpClient Admin, string BrandUrl)> NewWorkspaceAsync()
    {
        var admin = await factory.SignInAsync(DemoData.AdminB);
        var created = await admin.CreateWorkspaceAsync(withBrand: false);
        return (admin, $"/api/v1/organizations/{created.Id}/brand-profile");
    }
}
