using Microsoft.AspNetCore.Mvc;
using SocialMediaAutomation.Api.Contracts;
using SocialMediaAutomation.Api.Extensions;
using SocialMediaAutomation.Application.Organizations.Commands;
using SocialMediaAutomation.Application.Organizations.DTOs;
using SocialMediaAutomation.Application.Organizations.Services;

namespace SocialMediaAutomation.Api.Controllers;

[ApiController]
[Route("api/v1/organizations/{orgId:guid}/members")]
public sealed class OrganizationMembersController(OrganizationMemberService members) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<MemberDto>> List(Guid orgId, CancellationToken cancellationToken) =>
        members.ListAsync(orgId, cancellationToken);

    /// <summary>Admin only. Adds an existing account to the workspace (no invitation email in the MVP).</summary>
    [HttpPost]
    public async Task<ActionResult<MemberDto>> Add(Guid orgId, AddMemberRequest request, CancellationToken cancellationToken)
    {
        var added = await members.AddAsync(new AddMemberCommand(orgId, request.Email, request.Role!.Value), cancellationToken);
        Response.Headers.ETag = RowVersionHeaders.ToETag(added.RowVersion);
        return StatusCode(StatusCodes.Status201Created, added);
    }

    /// <summary>Admin only. Creates a new login with the Creator role; the Admin hands the credentials over (no email in the MVP).</summary>
    [HttpPost("creators")]
    public async Task<ActionResult<MemberDto>> CreateCreator(Guid orgId, CreateCreatorRequest request, CancellationToken cancellationToken)
    {
        var created = await members.CreateCreatorAsync(new CreateCreatorCommand(orgId, request.FullName, request.Email, request.Password), cancellationToken);
        Response.Headers.ETag = RowVersionHeaders.ToETag(created.RowVersion);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    /// <summary>Admin only. Requires If-Match. The last active Admin cannot be deactivated.</summary>
    [HttpPost("{memberId:guid}/deactivate")]
    public async Task<MemberDto> Deactivate(Guid orgId, Guid memberId, CancellationToken cancellationToken)
    {
        var updated = await members.DeactivateAsync(new DeactivateMemberCommand(orgId, memberId, RowVersionHeaders.ReadIfMatch(Request)), cancellationToken);
        Response.Headers.ETag = RowVersionHeaders.ToETag(updated.RowVersion);
        return updated;
    }

    /// <summary>Admin only. Requires If-Match with the member's last seen row version.</summary>
    [HttpPut("{memberId:guid}/role")]
    public async Task<MemberDto> ChangeRole(Guid orgId, Guid memberId, ChangeMemberRoleRequest request, CancellationToken cancellationToken)
    {
        var updated = await members.ChangeRoleAsync(
            new ChangeMemberRoleCommand(orgId, memberId, request.Role!.Value, RowVersionHeaders.ReadIfMatch(Request)),
            cancellationToken);
        Response.Headers.ETag = RowVersionHeaders.ToETag(updated.RowVersion);
        return updated;
    }
}
