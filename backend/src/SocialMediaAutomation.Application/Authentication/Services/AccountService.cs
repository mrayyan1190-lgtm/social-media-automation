using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.Audit.Services;
using SocialMediaAutomation.Application.Authentication.DTOs;
using SocialMediaAutomation.Application.Common.Authorization;
using SocialMediaAutomation.Domain.Common;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Application.Authentication.Services;

public sealed record RegisterCommand(string Email, string Password, string WorkspaceName);

public sealed record CompleteProfileCommand(Guid? OrganizationId, string FullName, string TimeZoneId);

public sealed class AccountService(
    OrganizationAuthorizer authorizer,
    IUserRepository users,
    IOrganizationRepository organizations,
    IBrandProfileRepository brandProfiles,
    ISocialAccountRepository socialAccounts,
    IPasswordHasher passwordHasher,
    ActivityRecorder activity,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public const int MinPasswordLength = 8;
    public const int FullNameMaxLength = 200;

    /// <summary>Returns the user id when the credentials are valid and the account is active; otherwise null.</summary>
    public async Task<Guid?> ValidateCredentialsAsync(string email, string password, CancellationToken cancellationToken) =>
        (await CheckCredentialsAsync(email, password, cancellationToken)).UserId;

    /// <summary>
    /// The user id when the credentials are valid and the account is active. On failure, the reason (INVALID_CREDENTIALS or
    /// ACCOUNT_DISABLED, only when the password was right) is for the sign-in log only: callers still answer with one generic message.
    /// </summary>
    public async Task<(Guid? UserId, Guid? KnownUserId, string? Failure)> CheckCredentialsAsync(string email, string password, CancellationToken cancellationToken)
    {
        var user = await users.FindByNormalizedEmailAsync(User.NormalizeEmail(email ?? ""), cancellationToken);
        if (user is null)
        {
            // Hash anyway so response time does not reveal whether the email exists.
            passwordHasher.Hash(password ?? "");
            return (null, null, Domain.Entities.SignInEvents.InvalidCredentials);
        }

        if (!passwordHasher.Verify(user.PasswordHash, password ?? ""))
            return (null, user.Id, Domain.Entities.SignInEvents.InvalidCredentials);
        return user.IsActive ? (user.Id, user.Id, null) : (null, user.Id, Domain.Entities.SignInEvents.AccountDisabled);
    }

    /// <summary>
    /// Creates the global User, its workspace and the user's Admin membership in one transaction. The name and time zone
    /// are entered on the onboarding profile step, so the user starts with an empty name and the workspace with UTC.
    /// </summary>
    public async Task<RegisteredUserDto> RegisterAsync(RegisterCommand command, CancellationToken cancellationToken)
    {
        var email = Guard.Required(command.Email, "Email", 256);
        if ((command.Password ?? "").Length < MinPasswordLength)
            throw Guard.Invalid("Password", $"must be at least {MinPasswordLength} characters");

        if (await users.FindByNormalizedEmailAsync(User.NormalizeEmail(email), cancellationToken) is not null)
            throw new BusinessException(ErrorCodes.EmailAlreadyRegistered, "An account with this email already exists. Sign in instead.");

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var user = User.Create(Guid.NewGuid(), email, "", passwordHasher.Hash(command.Password!), now);
        // Single-login MVP: the person who signs up writes and approves their own posts. Approval is still a human
        // decision on one exact version; teammates (Creator / Reviewer) are frozen, so no second person exists to approve.
        var organization = Organization.Create(Guid.NewGuid(), command.WorkspaceName, industry: "", defaultLanguage: null, "UTC",
            requireDistinctReviewer: false, now);
        users.Add(user);
        organizations.Add(organization);
        organizations.Add(OrganizationMember.Create(Guid.NewGuid(), organization.Id, user.Id, OrganizationRole.Admin, now));
        activity.Audit(organization.Id, user.Id, AuditActions.WorkspaceCreated, ActivityRecorder.OrganizationResource, organization.Id);

        // A concurrent sign-up with the same email hits the unique NormalizedEmail index and surfaces as a conflict.
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new RegisteredUserDto(user.Id, user.Email, organization.Id);
    }

    /// <summary>
    /// Google sign-in. Google has verified that the person owns the email, so it signs in the account with that email, or
    /// creates the account without a workspace: onboarding then asks for the workspace (the WORKSPACE step), with the email
    /// fixed to the Google one. Same result shape as <see cref="CheckCredentialsAsync"/>.
    /// </summary>
    public async Task<(Guid? UserId, Guid? KnownUserId, string? Failure)> SignInWithGoogleAsync(string? email, bool emailVerified,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email) || !emailVerified)
            return (null, null, Domain.Entities.SignInEvents.InvalidCredentials);
        if (await users.FindByNormalizedEmailAsync(User.NormalizeEmail(email), cancellationToken) is { } user)
            return user.IsActive ? (user.Id, user.Id, null) : (null, user.Id, Domain.Entities.SignInEvents.AccountDisabled);

        // A random password nobody knows: the account signs in with Google only.
        var unusable = passwordHasher.Hash(Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
        var created = User.Create(Guid.NewGuid(), Guard.Required(email, "Email", 256), "", unusable, timeProvider.GetUtcNow().UtcDateTime);
        users.Add(created);
        // A concurrent first sign-in with the same email hits the unique NormalizedEmail index and surfaces as a conflict.
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return (created.Id, created.Id, null);
    }

    /// <summary>
    /// Onboarding profile step: the user's full name and the workspace time zone. The time zone is a workspace setting,
    /// so it is only changed when the caller may manage that workspace's settings.
    /// </summary>
    public async Task<MeDto> CompleteProfileAsync(CompleteProfileCommand command, CancellationToken cancellationToken)
    {
        var userId = authorizer.RequireUserId();
        var user = await users.GetForUpdateAsync(userId, cancellationToken) ?? throw new BusinessException(ErrorCodes.Unauthorized, "Authentication is required.");
        user.CompleteProfile(command.FullName);

        var memberships = await organizations.ListActiveMembershipsForUserAsync(userId, cancellationToken);
        var membership = command.OrganizationId is { } id
            ? memberships.SingleOrDefault(m => m.OrganizationId == id) ?? throw new BusinessException(ErrorCodes.NotFound, "Organization not found.")
            : memberships.FirstOrDefault();
        if (membership is not null && RolePermissions.Has(membership.Role, Permission.ManageOrganizationSettings))
        {
            var access = await authorizer.AuthorizeAsync(membership.OrganizationId, Permission.ManageOrganizationSettings, cancellationToken);
            var organization = await organizations.GetAsync(access.OrganizationId, cancellationToken)
                ?? throw new BusinessException(ErrorCodes.NotFound, "Organization not found.");
            // ponytail: no If-Match here; the workspace was just created by this user. Settings edits keep the ETag check.
            organization.UpdateSettings(organization.Name, organization.Industry, organization.DefaultLanguage, command.TimeZoneId,
                organization.RequireDistinctReviewer, timeProvider.GetUtcNow().UtcDateTime);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await GetMeAsync(userId, cancellationToken);
    }

    /// <summary>For the sign-out log entry; null for an unknown user.</summary>
    public async Task<string?> FindEmailAsync(Guid userId, CancellationToken cancellationToken) =>
        (await users.GetAsync(userId, cancellationToken))?.Email;

    public Task<MeDto> GetMeAsync(CancellationToken cancellationToken) => GetMeAsync(authorizer.RequireUserId(), cancellationToken);

    /// <summary>For the login response, before the new session cookie identifies the caller.</summary>
    public async Task<MeDto> GetMeAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await users.GetAsync(userId, cancellationToken);
        if (user is not { IsActive: true })
            throw new BusinessException(ErrorCodes.Unauthorized, "Authentication is required.");

        var memberships = await organizations.ListActiveMembershipsForUserAsync(user.Id, cancellationToken);
        return new MeDto(user.Id, user.Email, user.DisplayName, memberships, await OnboardingAsync(user, memberships, null, cancellationToken),
            user.IsSystemAdmin);
    }

    /// <summary>The internal-area identity. Only safe profile fields: never the hash or any configuration.</summary>
    public async Task<SystemAdminMeDto> GetSystemAdminAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await users.GetAsync(userId, cancellationToken);
        if (user is not { IsActive: true, IsSystemAdmin: true })
            throw new BusinessException(ErrorCodes.Forbidden, "This area is restricted to internal system administrators.");
        return new SystemAdminMeDto(user.Id, user.DisplayName, user.Email, SystemRoles.SystemAdmin);
    }

    /// <summary>Onboarding progress for one workspace: the requested one, or the user's first when none is given.</summary>
    public async Task<OnboardingStatusDto> GetOnboardingAsync(Guid? organizationId, CancellationToken cancellationToken)
    {
        var userId = authorizer.RequireUserId();
        var user = await users.GetAsync(userId, cancellationToken) ?? throw new BusinessException(ErrorCodes.Unauthorized, "Authentication is required.");
        var memberships = await organizations.ListActiveMembershipsForUserAsync(userId, cancellationToken);
        return await OnboardingAsync(user, memberships, organizationId, cancellationToken);
    }

    private async Task<OnboardingStatusDto> OnboardingAsync(
        User user, IReadOnlyList<Organizations.DTOs.MembershipSummaryDto> memberships, Guid? organizationId, CancellationToken cancellationToken)
    {
        // Accounts without a workspace yet: a first Google sign-in, or a user whose memberships were all removed.
        if (memberships.Count == 0 && organizationId is null)
            return new OnboardingStatusDto(null, true, false, user.ProfileCompleted, false, false, false, OnboardingSteps.Workspace);

        var membership = organizationId is { } id
            ? memberships.SingleOrDefault(m => m.OrganizationId == id) ?? throw new BusinessException(ErrorCodes.NotFound, "Organization not found.")
            : memberships[0];
        var brandCompleted = await brandProfiles.GetByOrganizationAsync(membership.OrganizationId, cancellationToken) is not null;
        // Onboarding requires one real provider connection (simulated destinations do not count); it never blocks the campaign API itself.
        var connected = (await socialAccounts.ListAsync(membership.OrganizationId, null, cancellationToken))
            .Any(a => a.ConnectionStatus == ConnectionStatus.CONNECTED && a.ExecutionMode is ExecutionMode.LIVE or ExecutionMode.TEST_ACCOUNT);

        // A step is only required of members whose role can complete it (e.g. Admins); invited Creators and Reviewers
        // go straight to the dashboard instead of being stuck on a page they are not allowed to submit.
        var nextStep =
            !user.ProfileCompleted ? OnboardingSteps.Profile
            : !brandCompleted && RolePermissions.Has(membership.Role, Permission.ManageBrandProfile) ? OnboardingSteps.BrandProfile
            : !connected && RolePermissions.Has(membership.Role, Permission.ManageSocialAccounts) ? OnboardingSteps.SocialConnections
            : OnboardingSteps.Complete;

        return new OnboardingStatusDto(
            membership.OrganizationId,
            AccountCreated: true,
            WorkspaceCreated: true,
            ProfileCompleted: user.ProfileCompleted,
            BrandProfileCompleted: brandCompleted,
            SocialAccountsConnected: connected,
            CanCreateCampaign: brandCompleted && RolePermissions.Has(membership.Role, Permission.ManageCampaigns),
            NextStep: nextStep);
    }
}
