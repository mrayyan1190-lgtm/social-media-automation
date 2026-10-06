using Microsoft.AspNetCore.Mvc;
using SocialMediaAutomation.Api.Contracts;
using SocialMediaAutomation.Api.Extensions;
using SocialMediaAutomation.Application.Campaigns.Commands;
using SocialMediaAutomation.Application.Campaigns.DTOs;
using SocialMediaAutomation.Application.Campaigns.Services;

namespace SocialMediaAutomation.Api.Controllers;

/// <summary>Campaign folders. Their posts are listed through GET posts?campaignId=.</summary>
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
