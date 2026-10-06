using SocialMediaAutomation.Domain.Entities;

namespace SocialMediaAutomation.Application.Campaigns.Commands;

public sealed record CreateCampaignCommand(Guid OrganizationId, CampaignDetails Details);

public sealed record UpdateCampaignCommand(Guid OrganizationId, Guid CampaignId, CampaignDetails Details, byte[]? ExpectedRowVersion);

public sealed record ArchiveCampaignCommand(Guid OrganizationId, Guid CampaignId, byte[]? ExpectedRowVersion);
