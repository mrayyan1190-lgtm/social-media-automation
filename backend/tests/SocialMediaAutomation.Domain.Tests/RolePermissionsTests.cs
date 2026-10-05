using SocialMediaAutomation.Domain.Common;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Domain.Tests;

public class RolePermissionsTests
{
    [Theory]
    [InlineData(OrganizationRole.Admin, Permission.ViewOrganization, true)]
    [InlineData(OrganizationRole.Admin, Permission.ManageOrganizationSettings, true)]
    [InlineData(OrganizationRole.Admin, Permission.ViewMembers, true)]
    [InlineData(OrganizationRole.Admin, Permission.ManageMembers, true)]
    [InlineData(OrganizationRole.Creator, Permission.ViewOrganization, true)]
    [InlineData(OrganizationRole.Creator, Permission.ManageOrganizationSettings, false)]
    [InlineData(OrganizationRole.Creator, Permission.ViewMembers, true)]
    [InlineData(OrganizationRole.Creator, Permission.ManageMembers, false)]
    [InlineData(OrganizationRole.Reviewer, Permission.ViewOrganization, true)]
    [InlineData(OrganizationRole.Reviewer, Permission.ManageOrganizationSettings, false)]
    [InlineData(OrganizationRole.Reviewer, Permission.ViewMembers, true)]
    [InlineData(OrganizationRole.Reviewer, Permission.ManageMembers, false)]
    [InlineData(OrganizationRole.Admin, Permission.ManageBrandProfile, true)]
    [InlineData(OrganizationRole.Creator, Permission.ManageBrandProfile, false)]
    [InlineData(OrganizationRole.Reviewer, Permission.ManageBrandProfile, false)]
    [InlineData(OrganizationRole.Reviewer, Permission.ViewBrandProfile, true)]
    [InlineData(OrganizationRole.Admin, Permission.ManageCampaigns, true)]
    [InlineData(OrganizationRole.Creator, Permission.ManageCampaigns, true)]
    [InlineData(OrganizationRole.Reviewer, Permission.ManageCampaigns, false)]
    [InlineData(OrganizationRole.Reviewer, Permission.ViewCampaigns, true)]
    [InlineData(OrganizationRole.Admin, Permission.SchedulePublication, true)]
    [InlineData(OrganizationRole.Creator, Permission.SchedulePublication, true)]
    [InlineData(OrganizationRole.Reviewer, Permission.SchedulePublication, false)]
    public void Role_grants_exactly_the_expected_permissions(OrganizationRole role, Permission permission, bool expected) =>
        Assert.Equal(expected, RolePermissions.Has(role, permission));

    [Fact]
    public void Every_role_has_a_permission_entry() =>
        Assert.All(Enum.GetValues<OrganizationRole>(), role => RolePermissions.Has(role, Permission.ViewOrganization));
}
