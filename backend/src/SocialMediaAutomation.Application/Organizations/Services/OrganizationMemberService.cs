using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.Audit.Services;
using SocialMediaAutomation.Application.Authentication.Services;
using SocialMediaAutomation.Application.Common;
using SocialMediaAutomation.Application.Common.Authorization;
using SocialMediaAutomation.Application.Organizations.Commands;
using SocialMediaAutomation.Application.Organizations.DTOs;
using SocialMediaAutomation.Domain.Common;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Application.Organizations.Services;

public sealed class OrganizationMemberService(
    OrganizationAuthorizer authorizer,
    IOrganizationRepository organizations,
    IUserRepository users,
    IPasswordHasher passwordHasher,
    ActivityRecorder activity,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<MemberDto>> ListAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ViewMembers, cancellationToken);
        return await organizations.ListMembersAsync(access.OrganizationId, cancellationToken);
    }

    public async Task<MemberDto> ChangeRoleAsync(ChangeMemberRoleCommand command, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(command.OrganizationId, Permission.ManageMembers, cancellationToken);
        var member = await LoadMemberAsync(access, command.MemberId, command.ExpectedRowVersion, cancellationToken);

        if (member.Role == OrganizationRole.Admin && command.Role != OrganizationRole.Admin && member.IsActive)
            await EnsureAnotherActiveAdminAsync(access, cancellationToken);

        member.ChangeRole(command.Role);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await GetDtoAsync(access, member.Id, cancellationToken);
    }

    /// <summary>
    /// Adds an existing account to the workspace (MVP invitation: no email delivery, PRD §9). The person must have signed up
    /// already; a previously deactivated member is reactivated with the new role.
    /// </summary>
    public async Task<MemberDto> AddAsync(AddMemberCommand command, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(command.OrganizationId, Permission.ManageMembers, cancellationToken);
        var email = Guard.Required(command.Email, "Email", 256);
        var user = await users.FindByNormalizedEmailAsync(User.NormalizeEmail(email), cancellationToken);
        if (user is not { IsActive: true })
            throw new BusinessException(ErrorCodes.NotFound, "No account uses this email yet. Ask them to sign up first, then add them here.");

        var existing = await organizations.GetMembershipAsync(access.OrganizationId, user.Id, cancellationToken);
        if (existing is { IsActive: true })
            throw new BusinessException(ErrorCodes.AlreadyExists, "This person is already a member of the workspace.");

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var member = existing ?? OrganizationMember.Create(Guid.NewGuid(), access.OrganizationId, user.Id, command.Role, now);
        if (existing is null)
            organizations.Add(member);
        else
            existing.Reactivate(command.Role);

        activity.Audit(access.OrganizationId, access.UserId, AuditActions.MemberAdded, ActivityRecorder.OrganizationResource, access.OrganizationId,
            new Dictionary<string, string?> { ["memberUserId"] = user.Id.ToString(), ["role"] = command.Role.ToString() });
        activity.Notify(access.OrganizationId, [user.Id], NotificationType.TEAM_INVITATION, "Added to a workspace",
            $"You were added as {command.Role}.", ActivityRecorder.OrganizationResource, access.OrganizationId);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await GetDtoAsync(access, member.Id, cancellationToken);
    }

    /// <summary>
    /// Creates a new account with the Creator role in this workspace, in one transaction. The full name completes the
    /// profile, so the Creator signs in straight to Home. The email must not belong to an existing account.
    /// </summary>
    public async Task<MemberDto> CreateCreatorAsync(CreateCreatorCommand command, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(command.OrganizationId, Permission.ManageMembers, cancellationToken);
        var fullName = Guard.Required(command.FullName, "Full name", User.DisplayNameMaxLength);
        var email = Guard.Required(command.Email, "Email", 256);
        if ((command.Password ?? "").Length < AccountService.MinPasswordLength)
            throw Guard.Invalid("Password", $"must be at least {AccountService.MinPasswordLength} characters");
        if (await users.FindByNormalizedEmailAsync(User.NormalizeEmail(email), cancellationToken) is not null)
            throw new BusinessException(ErrorCodes.EmailAlreadyRegistered, "An account with this email already exists.");

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var user = User.Create(Guid.NewGuid(), email, fullName, passwordHasher.Hash(command.Password!), now);
        var member = OrganizationMember.Create(Guid.NewGuid(), access.OrganizationId, user.Id, OrganizationRole.Creator, now);
        users.Add(user);
        organizations.Add(member);
        activity.Audit(access.OrganizationId, access.UserId, AuditActions.MemberAdded, ActivityRecorder.OrganizationResource, access.OrganizationId,
            new Dictionary<string, string?> { ["memberUserId"] = user.Id.ToString(), ["role"] = nameof(OrganizationRole.Creator), ["accountCreated"] = "true" });

        // A concurrent creation with the same email hits the unique NormalizedEmail index and surfaces as a conflict.
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await GetDtoAsync(access, member.Id, cancellationToken);
    }

    /// <summary>Removes the member's access. The last active Admin cannot be deactivated.</summary>
    public async Task<MemberDto> DeactivateAsync(DeactivateMemberCommand command, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(command.OrganizationId, Permission.ManageMembers, cancellationToken);
        var member = await LoadMemberAsync(access, command.MemberId, command.ExpectedRowVersion, cancellationToken);
        if (!member.IsActive)
            return await GetDtoAsync(access, member.Id, cancellationToken);
        if (member.Role == OrganizationRole.Admin)
            await EnsureAnotherActiveAdminAsync(access, cancellationToken);

        member.Deactivate();
        activity.Audit(access.OrganizationId, access.UserId, AuditActions.MemberDeactivated, ActivityRecorder.OrganizationResource, access.OrganizationId,
            new Dictionary<string, string?> { ["memberUserId"] = member.UserId.ToString() });

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await GetDtoAsync(access, member.Id, cancellationToken);
    }

    private async Task<OrganizationMember> LoadMemberAsync(OrganizationAccess access, Guid memberId, byte[]? expectedRowVersion, CancellationToken cancellationToken)
    {
        var member = await organizations.GetMemberAsync(memberId, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.NotFound, "Member not found.");
        access.EnsureOwns(member.OrganizationId, "Member");
        ConcurrencyGuard.EnsureCurrent(member.RowVersion, expectedRowVersion);
        return member;
    }

    private async Task EnsureAnotherActiveAdminAsync(OrganizationAccess access, CancellationToken cancellationToken)
    {
        if (await organizations.CountActiveAdminsAsync(access.OrganizationId, cancellationToken) <= 1)
            throw new BusinessException(ErrorCodes.InvalidStateTransition, "An organization must keep at least one active Admin.");
    }

    private async Task<MemberDto> GetDtoAsync(OrganizationAccess access, Guid memberId, CancellationToken cancellationToken) =>
        (await organizations.ListMembersAsync(access.OrganizationId, cancellationToken)).Single(m => m.MemberId == memberId);
}
