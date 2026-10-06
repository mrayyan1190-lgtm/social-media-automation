using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.Audit.Services;
using SocialMediaAutomation.Application.Campaigns.Commands;
using SocialMediaAutomation.Application.Campaigns.DTOs;
using SocialMediaAutomation.Application.Common;
using SocialMediaAutomation.Application.Common.Authorization;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Application.Campaigns.Services;

/// <summary>Campaign folders: create, rename/recolour, archive. Posts are filed into them through the post commands.</summary>
public sealed class CampaignService(
    OrganizationAuthorizer authorizer,
    ICampaignRepository campaigns,
    IBrandProfileRepository brandProfiles,
    ActivityRecorder activity,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<CampaignSummaryDto>> ListAsync(Guid organizationId, bool includeArchived, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ViewCampaigns, cancellationToken);
        return await campaigns.ListAsync(access.OrganizationId, includeArchived, cancellationToken);
    }

    public async Task<CampaignDto> GetAsync(Guid organizationId, Guid campaignId, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ViewCampaigns, cancellationToken);
        return CampaignDto.From(await LoadAsync(access, campaignId, cancellationToken));
    }

    public async Task<CampaignDto> CreateAsync(CreateCampaignCommand command, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(command.OrganizationId, Permission.ManageCampaigns, cancellationToken);
        // Onboarding order: brand first, then content (the brand profile is what AI Studio writes with).
        if (await brandProfiles.GetByOrganizationAsync(access.OrganizationId, cancellationToken) is null)
            throw new BusinessException(ErrorCodes.ValidationFailed, "Complete the brand profile before creating campaigns.");
        var campaign = Campaign.Create(Guid.NewGuid(), access.OrganizationId, command.Details, access.UserId, Now);
        campaigns.Add(campaign);
        Audit(access, campaign, AuditActions.CampaignCreated, "created");

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return CampaignDto.From(campaign);
    }

    public async Task<CampaignDto> UpdateAsync(UpdateCampaignCommand command, CancellationToken cancellationToken)
    {
        var (access, campaign) = await LoadForChangeAsync(command.OrganizationId, command.CampaignId, command.ExpectedRowVersion, cancellationToken);
        campaign.Update(command.Details, access.UserId, Now);
        Audit(access, campaign, AuditActions.CampaignUpdated, "details");

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return CampaignDto.From(campaign);
    }

    public async Task<CampaignDto> ArchiveAsync(ArchiveCampaignCommand command, CancellationToken cancellationToken)
    {
        var (access, campaign) = await LoadForChangeAsync(command.OrganizationId, command.CampaignId, command.ExpectedRowVersion, cancellationToken);
        campaign.Archive(access.UserId, Now);
        Audit(access, campaign, AuditActions.CampaignUpdated, "archived");

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return CampaignDto.From(campaign);
    }

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    private async Task<Campaign> LoadAsync(OrganizationAccess access, Guid campaignId, CancellationToken cancellationToken)
    {
        var campaign = await campaigns.GetAsync(campaignId, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.NotFound, "Campaign not found.");
        access.EnsureOwns(campaign.OrganizationId, "Campaign");
        return campaign;
    }

    private async Task<(OrganizationAccess, Campaign)> LoadForChangeAsync(
        Guid organizationId, Guid campaignId, byte[]? expectedRowVersion, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ManageCampaigns, cancellationToken);
        var campaign = await LoadAsync(access, campaignId, cancellationToken);
        ConcurrencyGuard.EnsureCurrent(campaign.RowVersion, expectedRowVersion);
        return (access, campaign);
    }

    private void Audit(OrganizationAccess access, Campaign campaign, string action, string change) =>
        activity.Audit(access.OrganizationId, access.UserId, action, ActivityRecorder.CampaignResource, campaign.Id,
            new Dictionary<string, string?> { ["change"] = change });
}
