using Microsoft.AspNetCore.Mvc;
using SocialMediaAutomation.Api.Contracts;
using SocialMediaAutomation.Api.Extensions;
using SocialMediaAutomation.Application.Campaigns.Commands;
using SocialMediaAutomation.Application.Campaigns.DTOs;
using SocialMediaAutomation.Application.Campaigns.Services;

namespace SocialMediaAutomation.Api.Controllers;

[ApiController]
[Route("api/v1/organizations/{orgId:guid}/campaigns")]
public sealed class CampaignsController(CampaignService campaigns) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<CampaignSummaryDto>> List(Guid orgId, [FromQuery] bool includeArchived, CancellationToken cancellationToken) =>
        campaigns.ListAsync(orgId, includeArchived, cancellationToken);

    [HttpPost]
    public async Task<ActionResult<CampaignDto>> Create(Guid orgId, SaveCampaignRequest request, CancellationToken cancellationToken)
    {
        var campaign = await campaigns.CreateAsync(new CreateCampaignCommand(orgId, request.ToDetails()), cancellationToken);
        Response.Headers.ETag = RowVersionHeaders.ToETag(campaign.RowVersion);
        return CreatedAtAction(nameof(Get), new { orgId, campaignId = campaign.Id }, campaign);
    }

    [HttpGet("{campaignId:guid}")]
    public async Task<CampaignDto> Get(Guid orgId, Guid campaignId, CancellationToken cancellationToken) =>
        WithETag(await campaigns.GetAsync(orgId, campaignId, cancellationToken));

    /// <summary>Requires If-Match. Archived campaigns are read-only.</summary>
    [HttpPut("{campaignId:guid}")]
    public async Task<CampaignDto> Update(Guid orgId, Guid campaignId, SaveCampaignRequest request, CancellationToken cancellationToken) =>
        WithETag(await campaigns.UpdateAsync(
            new UpdateCampaignCommand(orgId, campaignId, request.ToDetails(), RowVersionHeaders.ReadIfMatch(Request)), cancellationToken));

    /// <summary>Requires If-Match. Replaces the selected platforms (at least one).</summary>
    [HttpPut("{campaignId:guid}/platforms")]
    public async Task<CampaignDto> SetPlatforms(Guid orgId, Guid campaignId, SetCampaignPlatformsRequest request, CancellationToken cancellationToken) =>
        WithETag(await campaigns.SetPlatformsAsync(
            new SetCampaignPlatformsCommand(orgId, campaignId, request.Platforms, RowVersionHeaders.ReadIfMatch(Request)), cancellationToken));

    /// <summary>Requires If-Match (the campaign's). Replaces the supporting facts with one fact per entry.</summary>
    [HttpPut("{campaignId:guid}/facts")]
    public async Task<CampaignDto> ReplaceFacts(Guid orgId, Guid campaignId, ReplaceCampaignFactsRequest request, CancellationToken cancellationToken) =>
        WithETag(await campaigns.ReplaceFactsAsync(
            new ReplaceCampaignFactsCommand(orgId, campaignId, request.SupportingFacts, RowVersionHeaders.ReadIfMatch(Request)), cancellationToken));

    /// <summary>The resolved brief AI generation would receive, with readiness and what is still missing.</summary>
    [HttpGet("{campaignId:guid}/review")]
    public async Task<CampaignBriefDto> Review(Guid orgId, Guid campaignId, CancellationToken cancellationToken)
    {
        var brief = await campaigns.GetBriefAsync(orgId, campaignId, cancellationToken);
        Response.Headers.ETag = RowVersionHeaders.ToETag(brief.RowVersion);
        return brief;
    }

    /// <summary>Requires If-Match.</summary>
    [HttpPost("{campaignId:guid}/archive")]
    public async Task<CampaignDto> Archive(Guid orgId, Guid campaignId, CancellationToken cancellationToken) =>
        WithETag(await campaigns.ArchiveAsync(
            new ArchiveCampaignCommand(orgId, campaignId, RowVersionHeaders.ReadIfMatch(Request)), cancellationToken));

    private CampaignDto WithETag(CampaignDto campaign)
    {
        Response.Headers.ETag = RowVersionHeaders.ToETag(campaign.RowVersion);
        return campaign;
    }
}
