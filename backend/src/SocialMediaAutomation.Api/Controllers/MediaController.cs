using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SocialMediaAutomation.Api.Extensions;
using SocialMediaAutomation.Application.Media.DTOs;
using SocialMediaAutomation.Application.Media.Services;
using SocialMediaAutomation.Domain.Common;

namespace SocialMediaAutomation.Api.Controllers;

[ApiController]
[Route("api/v1/organizations/{orgId:guid}/media")]
public sealed class MediaController(MediaService media) : ControllerBase
{
    // Transport cap slightly above the 8 MB image limit so oversize files get a VALIDATION_FAILED answer, not a dropped connection.
    private const long RequestLimit = PlatformRules.MaxImageBytes + 512 * 1024;

    /// <summary>Uploads one JPEG or PNG image (multipart field "file").</summary>
    [HttpPost]
    [EnableRateLimiting(RateLimitingExtensions.Upload)]
    [RequestSizeLimit(RequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = RequestLimit)]
    public async Task<ActionResult<MediaAssetDto>> Upload(Guid orgId, IFormFile file, CancellationToken cancellationToken)
    {
        await using var content = file.OpenReadStream();
        var asset = await media.UploadAsync(orgId, file.FileName, content, cancellationToken);
        return CreatedAtAction(nameof(Get), new { orgId, mediaAssetId = asset.Id }, asset);
    }

    /// <summary>Media Library: every asset of the workspace with its usage count.</summary>
    [HttpGet]
    public Task<IReadOnlyList<MediaLibraryItemDto>> List(Guid orgId, CancellationToken cancellationToken) =>
        media.ListAsync(orgId, cancellationToken);

    [HttpGet("{mediaAssetId:guid}")]
    public Task<MediaAssetDto> Get(Guid orgId, Guid mediaAssetId, CancellationToken cancellationToken) =>
        media.GetAsync(orgId, mediaAssetId, cancellationToken);

    /// <summary>Image bytes for in-app preview, behind the same membership check as every other read.</summary>
    [HttpGet("{mediaAssetId:guid}/content")]
    public async Task<IActionResult> Content(Guid orgId, Guid mediaAssetId, CancellationToken cancellationToken)
    {
        var (stream, contentType) = await media.OpenContentAsync(orgId, mediaAssetId, cancellationToken);
        Response.Headers.XContentTypeOptions = "nosniff";
        // The bytes behind an asset id never change, so the browser may keep them; "private" keeps shared caches out.
        Response.Headers.CacheControl = "private, max-age=86400, immutable";
        return File(stream, contentType);
    }
}
