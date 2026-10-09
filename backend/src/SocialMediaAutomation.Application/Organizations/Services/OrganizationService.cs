using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.Audit.Services;
using SocialMediaAutomation.Application.Common;
using SocialMediaAutomation.Application.Common.Authorization;
using SocialMediaAutomation.Application.Organizations.Commands;
using SocialMediaAutomation.Application.Organizations.DTOs;
using SocialMediaAutomation.Application.Platform;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Application.Organizations.Services;

public sealed class OrganizationService(
    OrganizationAuthorizer authorizer,
    IOrganizationRepository organizations,
    ActivityRecorder activity,
    PageTokenConnectionService pageTokens,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public Task<IReadOnlyList<MembershipSummaryDto>> ListMineAsync(CancellationToken cancellationToken) =>
        organizations.ListActiveMembershipsForUserAsync(authorizer.RequireUserId(), cancellationToken);

    /// <summary>Any signed-in user may create a workspace; the creator becomes its first Admin in the same transaction.</summary>
    public async Task<OrganizationDto> CreateAsync(CreateOrganizationCommand command, CancellationToken cancellationToken)
    {
        var userId = authorizer.RequireUserId();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var organization = Organization.Create(
            Guid.NewGuid(), command.Name, command.Industry, command.DefaultLanguage, command.TimeZoneId, command.RequireDistinctReviewer, now);
        organizations.Add(organization);
        organizations.Add(OrganizationMember.Create(Guid.NewGuid(), organization.Id, userId, OrganizationRole.Admin, now));
        activity.Audit(organization.Id, userId, AuditActions.WorkspaceCreated, ActivityRecorder.OrganizationResource, organization.Id);
        await pageTokens.AttachDefaultPageAsync(organization.Id, userId, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return OrganizationDto.From(organization, OrganizationRole.Admin);
    }

    public async Task<OrganizationDto> GetAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ViewOrganization, cancellationToken);
        return OrganizationDto.From(await LoadAsync(access, cancellationToken), access.Role);
    }

    public async Task<OrganizationDto> UpdateSettingsAsync(UpdateOrganizationSettingsCommand command, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(command.OrganizationId, Permission.ManageOrganizationSettings, cancellationToken);
        var organization = await LoadAsync(access, cancellationToken);

        ConcurrencyGuard.EnsureCurrent(organization.RowVersion, command.ExpectedRowVersion);
        organization.UpdateSettings(
            command.Name, command.Industry, command.DefaultLanguage, command.TimeZoneId, command.RequireDistinctReviewer, timeProvider.GetUtcNow().UtcDateTime);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return OrganizationDto.From(organization, access.Role);
    }

    private async Task<Organization> LoadAsync(OrganizationAccess access, CancellationToken cancellationToken) =>
        await organizations.GetAsync(access.OrganizationId, cancellationToken)
        ?? throw new BusinessException(ErrorCodes.NotFound, "Organization not found.");
}
