using Microsoft.AspNetCore.Mvc;
using SocialMediaAutomation.Api.Contracts;
using SocialMediaAutomation.Api.Extensions;
using SocialMediaAutomation.Application.BrandProfiles.DTOs;
using SocialMediaAutomation.Application.BrandProfiles.Services;

namespace SocialMediaAutomation.Api.Controllers;

[ApiController]
[Route("api/v1/organizations/{orgId:guid}/brand-profile")]
public sealed class BrandProfileController(BrandProfileService brandProfiles) : ControllerBase
{
    [HttpGet]
    public async Task<BrandProfileDto> Get(Guid orgId, CancellationToken cancellationToken)
    {
        var profile = await brandProfiles.GetAsync(orgId, cancellationToken);
        Response.Headers.ETag = RowVersionHeaders.ToETag(profile.RowVersion);
        return profile;
    }

    /// <summary>Admin only. Creates the profile when none exists (no If-Match); otherwise If-Match is required.</summary>
    [HttpPut]
    public async Task<BrandProfileDto> Save(Guid orgId, SaveBrandProfileRequest request, CancellationToken cancellationToken)
    {
        var profile = await brandProfiles.SaveAsync(orgId, request.ToDetails(), RowVersionHeaders.ReadIfMatch(Request), cancellationToken);
        Response.Headers.ETag = RowVersionHeaders.ToETag(profile.RowVersion);
        return profile;
    }
}
