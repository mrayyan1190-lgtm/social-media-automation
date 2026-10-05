using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.Audit.Services;
using SocialMediaAutomation.Application.Common.Authorization;
using SocialMediaAutomation.Application.SocialAccounts.DTOs;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Application.SocialAccounts.Services;

/// <summary>
/// Workspace destinations. This phase supports destinations without a provider connection (SIMULATED and
/// NOT_CONFIGURED); LIVE/TEST_ACCOUNT destinations arrive with OAuth connections (TASKS.md Phase 9).
/// </summary>
public sealed class SocialAccountService(
    OrganizationAuthorizer authorizer,
    ISocialAccountRepository accounts,
    ActivityRecorder activity,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<SocialAccountDto>> ListAsync(Guid organizationId, SocialPlatform? platform, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ViewSocialAccounts, cancellationToken);
        return await accounts.ListAsync(access.OrganizationId, platform, cancellationToken);
    }

    public async Task<SocialAccountDto> CreateUnconnectedAsync(
        Guid organizationId, SocialPlatform platform, string displayName, ExecutionMode mode, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ManageSocialAccounts, cancellationToken);
        var account = SocialAccount.CreateUnconnected(Guid.NewGuid(), access.OrganizationId, platform, displayName, mode, access.UserId,
            timeProvider.GetUtcNow().UtcDateTime);
        accounts.Add(account);
        activity.Audit(access.OrganizationId, access.UserId, AuditActions.SocialAccountCreated, ActivityRecorder.SocialAccountResource, account.Id,
            new Dictionary<string, string?> { ["platform"] = platform.ToString(), ["executionMode"] = mode.ToString() });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return SocialAccountDto.From(account);
    }
}
