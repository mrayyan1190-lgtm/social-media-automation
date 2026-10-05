using Microsoft.AspNetCore.Mvc;
using SocialMediaAutomation.Api.Contracts;
using SocialMediaAutomation.Api.Extensions;
using SocialMediaAutomation.Application.Campaigns.Commands;
using SocialMediaAutomation.Application.Campaigns.DTOs;
using SocialMediaAutomation.Application.Campaigns.Services;

namespace SocialMediaAutomation.Api.Controllers;

/// <summary>Facts are listed as part of GET campaign; these endpoints change them.</summary>
[ApiController]
[Route("api/v1/organizations/{orgId:guid}/campaigns/{campaignId:guid}/facts")]
public sealed class CampaignFactsController(CampaignService campaigns) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<CampaignFactDto>> Add(Guid orgId, Guid campaignId, SaveCampaignFactRequest request, CancellationToken cancellationToken)
    {
        var fact = await campaigns.AddFactAsync(new AddCampaignFactCommand(orgId, campaignId, request.Text, request.SourceNote), cancellationToken);
        Response.Headers.ETag = RowVersionHeaders.ToETag(fact.RowVersion);
        return StatusCode(StatusCodes.Status201Created, fact);
    }

    /// <summary>Requires If-Match.</summary>
    [HttpPut("{factId:guid}")]
    public async Task<CampaignFactDto> Update(Guid orgId, Guid campaignId, Guid factId, SaveCampaignFactRequest request, CancellationToken cancellationToken)
    {
        var fact = await campaigns.UpdateFactAsync(
            new UpdateCampaignFactCommand(orgId, campaignId, factId, request.Text, request.SourceNote, RowVersionHeaders.ReadIfMatch(Request)),
            cancellationToken);
        Response.Headers.ETag = RowVersionHeaders.ToETag(fact.RowVersion);
        return fact;
    }

    /// <summary>Requires If-Match. Soft delete: the fact disappears from the campaign but its id stays resolvable for provenance.</summary>
    [HttpDelete("{factId:guid}")]
    public async Task<IActionResult> Remove(Guid orgId, Guid campaignId, Guid factId, CancellationToken cancellationToken)
    {
        await campaigns.RemoveFactAsync(new RemoveCampaignFactCommand(orgId, campaignId, factId, RowVersionHeaders.ReadIfMatch(Request)), cancellationToken);
        return NoContent();
    }
}
