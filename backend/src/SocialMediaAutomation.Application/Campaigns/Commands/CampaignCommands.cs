using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Application.Campaigns.Commands;

public sealed record CreateCampaignCommand(Guid OrganizationId, CampaignDetails Details);

public sealed record UpdateCampaignCommand(Guid OrganizationId, Guid CampaignId, CampaignDetails Details, byte[]? ExpectedRowVersion);

public sealed record SetCampaignPlatformsCommand(Guid OrganizationId, Guid CampaignId, IReadOnlyCollection<SocialPlatform>? Platforms, byte[]? ExpectedRowVersion);

/// <summary>Replaces the campaign's supporting facts with one fact per line; unchanged lines keep their fact id.</summary>
public sealed record ReplaceCampaignFactsCommand(Guid OrganizationId, Guid CampaignId, IReadOnlyList<string>? SupportingFacts, byte[]? ExpectedRowVersion);

public sealed record ArchiveCampaignCommand(Guid OrganizationId, Guid CampaignId, byte[]? ExpectedRowVersion);

public sealed record AddCampaignFactCommand(Guid OrganizationId, Guid CampaignId, string Text, string? SourceNote);

public sealed record UpdateCampaignFactCommand(
    Guid OrganizationId, Guid CampaignId, Guid FactId, string Text, string? SourceNote, byte[]? ExpectedRowVersion);

public sealed record RemoveCampaignFactCommand(Guid OrganizationId, Guid CampaignId, Guid FactId, byte[]? ExpectedRowVersion);
