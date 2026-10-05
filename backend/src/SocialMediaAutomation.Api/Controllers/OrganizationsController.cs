using Microsoft.AspNetCore.Mvc;
using SocialMediaAutomation.Api.Contracts;
using SocialMediaAutomation.Api.Extensions;
using SocialMediaAutomation.Application.Organizations.Commands;
using SocialMediaAutomation.Application.Organizations.DTOs;
using SocialMediaAutomation.Application.Organizations.Services;

namespace SocialMediaAutomation.Api.Controllers;

[ApiController]
[Route("api/v1/organizations")]
public sealed class OrganizationsController(OrganizationService organizations) : ControllerBase
{
    /// <summary>Workspaces the caller is an active member of.</summary>
    [HttpGet]
    public Task<IReadOnlyList<MembershipSummaryDto>> List(CancellationToken cancellationToken) =>
        organizations.ListMineAsync(cancellationToken);

    [HttpPost]
    public async Task<ActionResult<OrganizationDto>> Create(CreateOrganizationRequest request, CancellationToken cancellationToken)
    {
        var created = await organizations.CreateAsync(
            new CreateOrganizationCommand(request.Name, request.Industry, request.DefaultLanguage, request.TimeZoneId, request.RequireDistinctReviewer),
            cancellationToken);
        Response.Headers.ETag = RowVersionHeaders.ToETag(created.RowVersion);
        return CreatedAtAction(nameof(Get), new { orgId = created.Id }, created);
    }

    [HttpGet("{orgId:guid}")]
    public async Task<OrganizationDto> Get(Guid orgId, CancellationToken cancellationToken)
    {
        var organization = await organizations.GetAsync(orgId, cancellationToken);
        Response.Headers.ETag = RowVersionHeaders.ToETag(organization.RowVersion);
        return organization;
    }

    /// <summary>Admin only. Requires If-Match with the ETag from the last read.</summary>
    [HttpPut("{orgId:guid}/settings")]
    public async Task<OrganizationDto> UpdateSettings(Guid orgId, UpdateOrganizationSettingsRequest request, CancellationToken cancellationToken)
    {
        var updated = await organizations.UpdateSettingsAsync(
            new UpdateOrganizationSettingsCommand(orgId, request.Name, request.Industry ?? "", request.DefaultLanguage, request.TimeZoneId,
                request.RequireDistinctReviewer!.Value, RowVersionHeaders.ReadIfMatch(Request)),
            cancellationToken);
        Response.Headers.ETag = RowVersionHeaders.ToETag(updated.RowVersion);
        return updated;
    }
}
