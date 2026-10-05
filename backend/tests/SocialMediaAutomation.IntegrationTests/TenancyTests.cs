using System.Net;
using System.Net.Http.Json;
using SocialMediaAutomation.Application.Authentication.DTOs;
using SocialMediaAutomation.Application.Organizations.DTOs;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Infrastructure.Persistence.Seed;
using SocialMediaAutomation.IntegrationTests.Infrastructure;

namespace SocialMediaAutomation.IntegrationTests;

/// <summary>
/// Tenant isolation and role enforcement through the real HTTP API, session cookie and SQL Server.
/// No test relies on the frontend hiding anything: every call below is a direct API request.
/// </summary>
[Collection(ApiCollection.Name)]
public class TenancyTests(ApiFactory factory)
{
    private static readonly string OrgA = $"/api/v1/organizations/{DemoData.OrganizationAId}";
    private static readonly string OrgB = $"/api/v1/organizations/{DemoData.OrganizationBId}";

    // ---- Authentication -------------------------------------------------------------------------

    [Fact]
    public async Task Unauthenticated_requests_are_rejected()
    {
        var anonymous = factory.CreateClient();

        await (await anonymous.GetAsync("/api/v1/me")).AssertErrorAsync(HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized);
        await (await anonymous.GetAsync(OrgA)).AssertErrorAsync(HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized);
        await (await anonymous.GetAsync($"{OrgA}/members")).AssertErrorAsync(HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized);
    }

    [Fact]
    public async Task Wrong_password_is_rejected()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { DemoData.CreatorA.Email, Password = "wrong" });

        await response.AssertErrorAsync(HttpStatusCode.Unauthorized, ErrorCodes.InvalidCredentials);
    }

    // ---- 1. Member can access their organization ------------------------------------------------

    [Fact]
    public async Task Member_can_read_own_organization_and_members()
    {
        var reviewer = await factory.SignInAsync(DemoData.ReviewerA);

        var response = await reviewer.GetAsync(OrgA);
        var organization = await response.ReadAsync<OrganizationDto>();
        var members = await (await reviewer.GetAsync($"{OrgA}/members")).ReadAsync<List<MemberDto>>();

        Assert.Equal(DemoData.OrganizationAId, organization.Id);
        Assert.Equal(OrganizationRole.Reviewer, organization.MyRole);
        Assert.True(organization.RequireDistinctReviewer);
        Assert.Equal("Europe/London", organization.TimeZoneId);
        Assert.Equal(ApiClient.ETag(organization.RowVersion), response.Headers.ETag?.ToString());
        Assert.Equal(
            [DemoData.AdminA.Id, DemoData.CreatorA.Id, DemoData.ReviewerA.Id],
            members.Select(m => m.UserId).Order().ToArray());
    }

    [Fact]
    public async Task Me_and_organization_list_contain_only_own_memberships()
    {
        var creatorA = await factory.SignInAsync(DemoData.CreatorA);

        var me = await (await creatorA.GetAsync("/api/v1/me")).ReadAsync<MeDto>();
        var organizations = await (await creatorA.GetAsync("/api/v1/organizations")).ReadAsync<List<MembershipSummaryDto>>();

        Assert.Equal(DemoData.CreatorA.Id, me.UserId);
        var membership = Assert.Single(me.Memberships);
        Assert.Equal((DemoData.OrganizationAId, OrganizationRole.Creator), (membership.OrganizationId, membership.Role));
        Assert.DoesNotContain(organizations, o => o.OrganizationId == DemoData.OrganizationBId);
    }

    // ---- 2. Non-member cannot access; 3. cross-organization read fails ----------------------------

    [Fact]
    [Trait("Category", "Acceptance")]
    [Trait("Acceptance", "01 cross-tenant read denied")]
    public async Task Non_member_cannot_read_another_organization()
    {
        var adminB = await factory.SignInAsync(DemoData.AdminB);

        await (await adminB.GetAsync(OrgA)).AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
        await (await adminB.GetAsync($"{OrgA}/members")).AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Existing_foreign_organization_is_indistinguishable_from_unknown_id()
    {
        var adminB = await factory.SignInAsync(DemoData.AdminB);

        var foreign = await adminB.GetAsync(OrgA);
        var unknown = await adminB.GetAsync($"/api/v1/organizations/{Guid.NewGuid()}");

        await foreign.AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
        await unknown.AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    // ---- 4. Cross-organization mutation fails -----------------------------------------------------

    [Fact]
    [Trait("Category", "Acceptance")]
    [Trait("Acceptance", "02 cross-tenant mutation denied")]
    public async Task Admin_of_another_organization_cannot_change_settings_even_with_valid_etag()
    {
        var adminA = await factory.SignInAsync(DemoData.AdminA);
        var adminB = await factory.SignInAsync(DemoData.AdminB);
        var before = await (await adminA.GetAsync(OrgA)).ReadAsync<OrganizationDto>();

        var response = await adminB.PutJsonAsync($"{OrgA}/settings", SettingsBody(before, name: "Hijacked"), ApiClient.ETag(before.RowVersion));

        await response.AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
        var after = await (await adminA.GetAsync(OrgA)).ReadAsync<OrganizationDto>();
        Assert.Equal(before.Name, after.Name);
        Assert.Equal(before.RowVersion, after.RowVersion);
    }

    [Fact]
    public async Task Admin_cannot_change_a_foreign_member_through_own_organization_route()
    {
        var adminA = await factory.SignInAsync(DemoData.AdminA);
        var adminB = await factory.SignInAsync(DemoData.AdminB);
        var memberOfB = Assert.Single(await (await adminB.GetAsync($"{OrgB}/members")).ReadAsync<List<MemberDto>>());

        // Org A admin, org A route, org B member id: the member must not be found inside org A.
        var response = await adminA.PutJsonAsync(
            $"{OrgA}/members/{memberOfB.MemberId}/role", new { Role = "Reviewer" }, ApiClient.ETag(memberOfB.RowVersion));

        await response.AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
        var unchanged = Assert.Single(await (await adminB.GetAsync($"{OrgB}/members")).ReadAsync<List<MemberDto>>());
        Assert.Equal(OrganizationRole.Admin, unchanged.Role);
    }

    [Fact]
    public async Task Admin_cannot_change_member_of_another_organization_through_that_organization_route()
    {
        var adminA = await factory.SignInAsync(DemoData.AdminA);
        var adminB = await factory.SignInAsync(DemoData.AdminB);
        var reviewerA = (await (await adminA.GetAsync($"{OrgA}/members")).ReadAsync<List<MemberDto>>())
            .Single(m => m.UserId == DemoData.ReviewerA.Id);

        var response = await adminB.PutJsonAsync(
            $"{OrgA}/members/{reviewerA.MemberId}/role", new { Role = "Admin" }, ApiClient.ETag(reviewerA.RowVersion));

        await response.AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    // ---- 5. Role permissions are enforced server-side ---------------------------------------------

    [Theory]
    [InlineData("creator")]
    [InlineData("reviewer")]
    public async Task Non_admin_cannot_change_organization_settings(string who)
    {
        var client = await factory.SignInAsync(who == "creator" ? DemoData.CreatorA : DemoData.ReviewerA);
        var organization = await (await client.GetAsync(OrgA)).ReadAsync<OrganizationDto>();

        var response = await client.PutJsonAsync($"{OrgA}/settings", SettingsBody(organization, requireDistinctReviewer: false), ApiClient.ETag(organization.RowVersion));

        await response.AssertErrorAsync(HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task Creator_cannot_promote_self_to_admin()
    {
        var creator = await factory.SignInAsync(DemoData.CreatorA);
        var self = (await (await creator.GetAsync($"{OrgA}/members")).ReadAsync<List<MemberDto>>()).Single(m => m.UserId == DemoData.CreatorA.Id);

        var response = await creator.PutJsonAsync($"{OrgA}/members/{self.MemberId}/role", new { Role = "Admin" }, ApiClient.ETag(self.RowVersion));

        await response.AssertErrorAsync(HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task Admin_changes_member_role_and_stale_etag_is_rejected()
    {
        var adminA = await factory.SignInAsync(DemoData.AdminA);
        var reviewer = (await (await adminA.GetAsync($"{OrgA}/members")).ReadAsync<List<MemberDto>>()).Single(m => m.UserId == DemoData.ReviewerA.Id);
        var staleETag = ApiClient.ETag(reviewer.RowVersion);

        var promoted = await (await adminA.PutJsonAsync($"{OrgA}/members/{reviewer.MemberId}/role", new { Role = "Creator" }, staleETag)).ReadAsync<MemberDto>();
        var staleWrite = await adminA.PutJsonAsync($"{OrgA}/members/{reviewer.MemberId}/role", new { Role = "Admin" }, staleETag);
        var restored = await (await adminA.PutJsonAsync($"{OrgA}/members/{reviewer.MemberId}/role", new { Role = "Reviewer" }, ApiClient.ETag(promoted.RowVersion))).ReadAsync<MemberDto>();

        Assert.Equal(OrganizationRole.Creator, promoted.Role);
        await staleWrite.AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.ConcurrencyConflict);
        Assert.Equal(OrganizationRole.Reviewer, restored.Role);
    }

    [Fact]
    public async Task Admin_settings_update_requires_current_etag()
    {
        var adminA = await factory.SignInAsync(DemoData.AdminA);
        var original = await (await adminA.GetAsync(OrgA)).ReadAsync<OrganizationDto>();

        var missingIfMatch = await adminA.PutJsonAsync($"{OrgA}/settings", SettingsBody(original, name: "Renamed"), ifMatch: null);
        var renamed = await (await adminA.PutJsonAsync($"{OrgA}/settings", SettingsBody(original, name: "Renamed"), ApiClient.ETag(original.RowVersion))).ReadAsync<OrganizationDto>();
        var staleWrite = await adminA.PutJsonAsync($"{OrgA}/settings", SettingsBody(original, name: "Lost update"), ApiClient.ETag(original.RowVersion));
        await (await adminA.PutJsonAsync($"{OrgA}/settings", SettingsBody(original), ApiClient.ETag(renamed.RowVersion))).ReadAsync<OrganizationDto>();

        await missingIfMatch.AssertErrorAsync(HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        Assert.Equal("Renamed", renamed.Name);
        await staleWrite.AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.ConcurrencyConflict);
    }

    [Fact]
    public async Task Last_admin_cannot_be_demoted()
    {
        var adminB = await factory.SignInAsync(DemoData.AdminB);
        var self = Assert.Single(await (await adminB.GetAsync($"{OrgB}/members")).ReadAsync<List<MemberDto>>());

        var response = await adminB.PutJsonAsync($"{OrgB}/members/{self.MemberId}/role", new { Role = "Creator" }, ApiClient.ETag(self.RowVersion));

        await response.AssertErrorAsync(HttpStatusCode.Conflict, ErrorCodes.InvalidStateTransition);
    }

    // ---- Workspace creation and validation --------------------------------------------------------

    [Fact]
    public async Task Creating_an_organization_makes_the_caller_its_admin_and_keeps_it_isolated()
    {
        var adminB = await factory.SignInAsync(DemoData.AdminB);
        var creatorA = await factory.SignInAsync(DemoData.CreatorA);

        var response = await adminB.PostAsJsonAsync("/api/v1/organizations",
            new { Name = "B Second Workspace", Industry = "Retail", DefaultLanguage = "en-GB", TimeZoneId = "Asia/Kolkata", RequireDistinctReviewer = true });
        var created = await response.ReadAsync<OrganizationDto>();
        var me = await (await adminB.GetAsync("/api/v1/me")).ReadAsync<MeDto>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(OrganizationRole.Admin, created.MyRole);
        Assert.Contains(me.Memberships, m => m.OrganizationId == created.Id && m.Role == OrganizationRole.Admin);
        await (await creatorA.GetAsync($"/api/v1/organizations/{created.Id}")).AssertErrorAsync(HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Invalid_workspace_settings_and_unknown_roles_are_rejected()
    {
        var adminA = await factory.SignInAsync(DemoData.AdminA);
        var members = await (await adminA.GetAsync($"{OrgA}/members")).ReadAsync<List<MemberDto>>();
        var reviewer = members.Single(m => m.UserId == DemoData.ReviewerA.Id);

        var windowsZone = await adminA.PostAsJsonAsync("/api/v1/organizations",
            new { Name = "Bad zone", Industry = "Retail", DefaultLanguage = "en", TimeZoneId = "Pacific Standard Time" });
        var unknownRole = await adminA.PutJsonAsync($"{OrgA}/members/{reviewer.MemberId}/role", new { Role = "Owner" }, ApiClient.ETag(reviewer.RowVersion));

        await windowsZone.AssertErrorAsync(HttpStatusCode.BadRequest, ErrorCodes.InvalidTimeZone);
        await unknownRole.AssertErrorAsync(HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
    }

    private static object SettingsBody(OrganizationDto organization, string? name = null, bool? requireDistinctReviewer = null) => new
    {
        Name = name ?? organization.Name,
        organization.Industry,
        organization.DefaultLanguage,
        organization.TimeZoneId,
        RequireDistinctReviewer = requireDistinctReviewer ?? organization.RequireDistinctReviewer,
    };
}
