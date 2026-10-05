using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.Audit.Services;
using SocialMediaAutomation.Application.Campaigns.Commands;
using SocialMediaAutomation.Application.Campaigns.DTOs;
using SocialMediaAutomation.Application.Common;
using SocialMediaAutomation.Application.Common.Authorization;
using SocialMediaAutomation.Domain.Common;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Application.Campaigns.Services;

public sealed class CampaignService(
    OrganizationAuthorizer authorizer,
    ICampaignRepository campaigns,
    IBrandProfileRepository brandProfiles,
    IOrganizationRepository organizations,
    ActivityRecorder activity,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public const int MaxSupportingFacts = 50;

    public async Task<IReadOnlyList<CampaignSummaryDto>> ListAsync(Guid organizationId, bool includeArchived, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ViewCampaigns, cancellationToken);
        return await campaigns.ListAsync(access.OrganizationId, includeArchived, cancellationToken);
    }

    public async Task<CampaignDto> GetAsync(Guid organizationId, Guid campaignId, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ViewCampaigns, cancellationToken);
        return await ToDtoAsync(access, await LoadAsync(access, campaignId, cancellationToken), cancellationToken);
    }

    /// <summary>Requires the brand profile: it supplies the default audience and tone (onboarding step before campaigns).</summary>
    public async Task<CampaignDto> CreateAsync(CreateCampaignCommand command, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(command.OrganizationId, Permission.ManageCampaigns, cancellationToken);
        var brand = await brandProfiles.GetByOrganizationAsync(access.OrganizationId, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.ValidationFailed, "Complete the brand profile before creating campaigns.");
        var campaign = Campaign.Create(Guid.NewGuid(), access.OrganizationId, command.Details, brand.TargetAudience, access.UserId, Now);
        campaigns.Add(campaign);
        Audit(access, campaign, AuditActions.CampaignCreated, "created");

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(access, campaign, cancellationToken);
    }

    public async Task<CampaignDto> UpdateAsync(UpdateCampaignCommand command, CancellationToken cancellationToken)
    {
        var (access, campaign) = await LoadForChangeAsync(command.OrganizationId, command.CampaignId, command.ExpectedRowVersion, cancellationToken);
        campaign.Update(command.Details, await BrandAudienceAsync(access, cancellationToken), access.UserId, Now);
        Audit(access, campaign, AuditActions.CampaignUpdated, "details");

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(access, campaign, cancellationToken);
    }

    public async Task<CampaignDto> SetPlatformsAsync(SetCampaignPlatformsCommand command, CancellationToken cancellationToken)
    {
        var (access, campaign) = await LoadForChangeAsync(command.OrganizationId, command.CampaignId, command.ExpectedRowVersion, cancellationToken);
        campaign.SetPlatforms(command.Platforms, access.UserId, Now);
        Audit(access, campaign, AuditActions.CampaignUpdated, "platforms");

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(access, campaign, cancellationToken);
    }

    /// <summary>
    /// Replaces the supporting facts with the given lines. Lines that already exist keep their fact id (so earlier AI
    /// snapshots stay resolvable); dropped lines are soft-removed; new lines become facts without a source note.
    /// </summary>
    public async Task<CampaignDto> ReplaceFactsAsync(ReplaceCampaignFactsCommand command, CancellationToken cancellationToken)
    {
        var (access, campaign) = await LoadForChangeAsync(command.OrganizationId, command.CampaignId, command.ExpectedRowVersion, cancellationToken);
        campaign.EnsureEditable();
        var wanted = Guard.TextList(command.SupportingFacts, "Supporting facts", MaxSupportingFacts, CampaignFact.TextMax);
        var existing = await campaigns.ListActiveFactEntitiesAsync(access.OrganizationId, campaign.Id, cancellationToken);

        foreach (var fact in existing.Where(f => !wanted.Contains(f.Text, StringComparer.OrdinalIgnoreCase)))
            fact.Remove(campaign, Now);
        // Facts are listed by creation time; one millisecond apart (the column precision) keeps the user's line order.
        var now = Now;
        foreach (var (text, index) in wanted.Where(t => !existing.Any(f => string.Equals(f.Text, t, StringComparison.OrdinalIgnoreCase))).Select((t, i) => (t, i)))
            campaigns.Add(CampaignFact.Create(Guid.NewGuid(), campaign, text, sourceNote: null, access.UserId, now.AddMilliseconds(index)));
        campaign.Touch(access.UserId, Now);
        Audit(access, campaign, AuditActions.CampaignUpdated, "facts");

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(access, campaign, cancellationToken);
    }

    public async Task<CampaignDto> ArchiveAsync(ArchiveCampaignCommand command, CancellationToken cancellationToken)
    {
        var (access, campaign) = await LoadForChangeAsync(command.OrganizationId, command.CampaignId, command.ExpectedRowVersion, cancellationToken);
        campaign.Archive(access.UserId, Now);
        Audit(access, campaign, AuditActions.CampaignUpdated, "archived");

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(access, campaign, cancellationToken);
    }

    /// <summary>The resolved brief AI generation would receive, and what (if anything) still blocks generation.</summary>
    public async Task<CampaignBriefDto> GetBriefAsync(Guid organizationId, Guid campaignId, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ViewCampaigns, cancellationToken);
        var campaign = await LoadAsync(access, campaignId, cancellationToken);
        var brand = await brandProfiles.GetByOrganizationAsync(access.OrganizationId, cancellationToken);
        var organization = await organizations.GetAsync(access.OrganizationId, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.NotFound, "Organization not found.");
        var facts = await campaigns.ListFactsAsync(access.OrganizationId, campaign.Id, cancellationToken);
        var gaps = campaign.GenerationGaps(brandProfileExists: brand is not null);

        return new CampaignBriefDto(
            new BriefCampaignDto(campaign.Id, campaign.Name, campaign.Objective, campaign.Description, campaign.TargetAudience, campaign.Status),
            brand is null ? null : new BriefBrandContextDto(organization.Name, organization.Industry, brand.BusinessDescription, brand.BrandTone, brand.Website),
            new BriefCallToActionDto(campaign.CallToAction, campaign.CallToActionUrl),
            facts.Select(f => f.Text).ToList(),
            campaign.Platforms,
            new BriefPreferencesDto(campaign.ToneOverride ?? brand?.BrandTone, campaign.ContentLength, campaign.HashtagMode, campaign.EmojiMode),
            gaps.Count == 0,
            gaps,
            campaign.RowVersion);
    }

    public async Task<CampaignFactDto> AddFactAsync(AddCampaignFactCommand command, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(command.OrganizationId, Permission.ManageCampaigns, cancellationToken);
        var campaign = await LoadAsync(access, command.CampaignId, cancellationToken);

        var fact = CampaignFact.Create(Guid.NewGuid(), campaign, command.Text, command.SourceNote, access.UserId, Now);
        campaigns.Add(fact);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await FactDtoAsync(access, fact, cancellationToken);
    }

    public async Task<CampaignFactDto> UpdateFactAsync(UpdateCampaignFactCommand command, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(command.OrganizationId, Permission.ManageCampaigns, cancellationToken);
        var campaign = await LoadAsync(access, command.CampaignId, cancellationToken);
        var fact = await LoadFactAsync(access, campaign, command.FactId, cancellationToken);

        ConcurrencyGuard.EnsureCurrent(fact.RowVersion, command.ExpectedRowVersion);
        fact.Update(campaign, command.Text, command.SourceNote, access.UserId, Now);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await FactDtoAsync(access, fact, cancellationToken);
    }

    public async Task RemoveFactAsync(RemoveCampaignFactCommand command, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(command.OrganizationId, Permission.ManageCampaigns, cancellationToken);
        var campaign = await LoadAsync(access, command.CampaignId, cancellationToken);
        var fact = await LoadFactAsync(access, campaign, command.FactId, cancellationToken);

        ConcurrencyGuard.EnsureCurrent(fact.RowVersion, command.ExpectedRowVersion);
        fact.Remove(campaign, Now);

        await unitOfWork.SaveChangesAsync(cancellationToken);
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

    /// <summary>A fact is addressable only through its own campaign, inside its own organization, and only while not removed.</summary>
    private async Task<CampaignFact> LoadFactAsync(OrganizationAccess access, Campaign campaign, Guid factId, CancellationToken cancellationToken)
    {
        var fact = await campaigns.GetFactAsync(factId, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.NotFound, "Fact not found.");
        access.EnsureOwns(fact.OrganizationId, "Fact");
        if (fact.CampaignId != campaign.Id || fact.IsRemoved)
            throw new BusinessException(ErrorCodes.NotFound, "Fact not found.");
        return fact;
    }

    private async Task<string?> BrandAudienceAsync(OrganizationAccess access, CancellationToken cancellationToken) =>
        (await brandProfiles.GetByOrganizationAsync(access.OrganizationId, cancellationToken))?.TargetAudience;

    private void Audit(OrganizationAccess access, Campaign campaign, string action, string change) =>
        activity.Audit(access.OrganizationId, access.UserId, action, ActivityRecorder.CampaignResource, campaign.Id,
            new Dictionary<string, string?> { ["change"] = change });

    private async Task<CampaignDto> ToDtoAsync(OrganizationAccess access, Campaign campaign, CancellationToken cancellationToken) =>
        CampaignDto.From(campaign, await campaigns.ListFactsAsync(access.OrganizationId, campaign.Id, cancellationToken));

    private async Task<CampaignFactDto> FactDtoAsync(OrganizationAccess access, CampaignFact fact, CancellationToken cancellationToken) =>
        (await campaigns.ListFactsAsync(access.OrganizationId, fact.CampaignId, cancellationToken)).Single(f => f.Id == fact.Id);
}
