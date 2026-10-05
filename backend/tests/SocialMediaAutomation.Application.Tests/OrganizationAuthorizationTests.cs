using SocialMediaAutomation.Application.Common.Authorization;
using SocialMediaAutomation.Application.Organizations.Commands;
using SocialMediaAutomation.Application.Organizations.Services;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Application.Tests;

public class OrganizationAuthorizationTests
{
    private readonly FakeOrganizationRepository repository = new();
    private readonly Guid organizationA = Guid.NewGuid();
    private readonly Guid organizationB = Guid.NewGuid();

    private OrganizationAuthorizer AuthorizerFor(Guid? userId) => new(new FakeCurrentUser(userId), repository);

    // Role changes use only the authorizer, repository and unit of work; adding members and audit are not exercised here.
    private OrganizationMemberService MemberService(Guid adminId, FakeUnitOfWork unitOfWork) =>
        new(AuthorizerFor(adminId), repository, null!, null!, unitOfWork, TimeProvider.System);

    [Fact]
    public async Task Anonymous_caller_is_unauthorized()
    {
        var error = await Assert.ThrowsAsync<BusinessException>(() =>
            AuthorizerFor(null).AuthorizeAsync(organizationA, Permission.ViewOrganization, CancellationToken.None));

        Assert.Equal(ErrorCodes.Unauthorized, error.Code);
    }

    [Fact]
    public async Task Non_member_gets_not_found_even_when_organization_exists()
    {
        repository.AddMember(organizationA, Guid.NewGuid(), OrganizationRole.Admin);

        var error = await Assert.ThrowsAsync<BusinessException>(() =>
            AuthorizerFor(Guid.NewGuid()).AuthorizeAsync(organizationA, Permission.ViewOrganization, CancellationToken.None));

        Assert.Equal(ErrorCodes.NotFound, error.Code);
    }

    [Fact]
    public async Task Membership_in_another_organization_grants_nothing()
    {
        var userId = Guid.NewGuid();
        repository.AddMember(organizationB, userId, OrganizationRole.Admin);

        var error = await Assert.ThrowsAsync<BusinessException>(() =>
            AuthorizerFor(userId).AuthorizeAsync(organizationA, Permission.ViewOrganization, CancellationToken.None));

        Assert.Equal(ErrorCodes.NotFound, error.Code);
    }

    [Theory]
    [InlineData(OrganizationRole.Creator)]
    [InlineData(OrganizationRole.Reviewer)]
    public async Task Member_without_permission_is_forbidden(OrganizationRole role)
    {
        var userId = Guid.NewGuid();
        repository.AddMember(organizationA, userId, role);

        var error = await Assert.ThrowsAsync<BusinessException>(() =>
            AuthorizerFor(userId).AuthorizeAsync(organizationA, Permission.ManageMembers, CancellationToken.None));

        Assert.Equal(ErrorCodes.Forbidden, error.Code);
    }

    [Fact]
    public async Task Member_with_permission_receives_scoped_access()
    {
        var userId = Guid.NewGuid();
        var member = repository.AddMember(organizationA, userId, OrganizationRole.Admin);

        var access = await AuthorizerFor(userId).AuthorizeAsync(organizationA, Permission.ManageMembers, CancellationToken.None);

        Assert.Equal(new OrganizationAccess(organizationA, userId, member.Id, OrganizationRole.Admin), access);
    }

    [Fact]
    public void Resource_from_another_organization_is_reported_as_not_found()
    {
        var access = new OrganizationAccess(organizationA, Guid.NewGuid(), Guid.NewGuid(), OrganizationRole.Admin);

        var error = Assert.Throws<BusinessException>(() => access.EnsureOwns(organizationB, "Member"));

        Assert.Equal(ErrorCodes.NotFound, error.Code);
    }

    [Fact]
    public async Task Admin_cannot_change_role_of_member_in_another_organization_via_own_organization()
    {
        var adminId = Guid.NewGuid();
        repository.AddMember(organizationA, adminId, OrganizationRole.Admin);
        var foreignMember = repository.AddMember(organizationB, Guid.NewGuid(), OrganizationRole.Reviewer);
        var unitOfWork = new FakeUnitOfWork();
        var service = MemberService(adminId, unitOfWork);

        var error = await Assert.ThrowsAsync<BusinessException>(() => service.ChangeRoleAsync(
            new ChangeMemberRoleCommand(organizationA, foreignMember.Id, OrganizationRole.Admin, foreignMember.RowVersion),
            CancellationToken.None));

        Assert.Equal(ErrorCodes.NotFound, error.Code);
        Assert.Equal(OrganizationRole.Reviewer, foreignMember.Role);
        Assert.Equal(0, unitOfWork.Saves);
    }

    [Fact]
    public async Task Last_active_admin_cannot_be_demoted()
    {
        var adminId = Guid.NewGuid();
        var admin = repository.AddMember(organizationA, adminId, OrganizationRole.Admin);
        var service = MemberService(adminId, new FakeUnitOfWork());

        var error = await Assert.ThrowsAsync<BusinessException>(() => service.ChangeRoleAsync(
            new ChangeMemberRoleCommand(organizationA, admin.Id, OrganizationRole.Creator, admin.RowVersion), CancellationToken.None));

        Assert.Equal(ErrorCodes.InvalidStateTransition, error.Code);
        Assert.Equal(OrganizationRole.Admin, admin.Role);
    }

    [Fact]
    public async Task Role_change_without_expected_row_version_is_rejected()
    {
        var adminId = Guid.NewGuid();
        repository.AddMember(organizationA, adminId, OrganizationRole.Admin);
        var reviewer = repository.AddMember(organizationA, Guid.NewGuid(), OrganizationRole.Reviewer);
        var service = MemberService(adminId, new FakeUnitOfWork());

        var error = await Assert.ThrowsAsync<BusinessException>(() => service.ChangeRoleAsync(
            new ChangeMemberRoleCommand(organizationA, reviewer.Id, OrganizationRole.Creator, null), CancellationToken.None));

        Assert.Equal(ErrorCodes.ValidationFailed, error.Code);
    }
}
