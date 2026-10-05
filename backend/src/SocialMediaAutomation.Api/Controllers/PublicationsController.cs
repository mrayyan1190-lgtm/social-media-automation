using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using SocialMediaAutomation.Api.Extensions;
using SocialMediaAutomation.Application.Publications.DTOs;
using SocialMediaAutomation.Application.Publications.Services;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Api.Controllers;

/// <summary>Wall-clock input: local date ("2026-11-01"), local time ("09:30:00") and an IANA zone. The server resolves UTC.</summary>
public sealed record ScheduleTimeRequest([Required] DateOnly? LocalDate, [Required] TimeOnly? LocalTime, [Required] string? TimeZone, int? UtcOffsetMinutes)
{
    public LocalSchedule ToSchedule() => new(LocalDate!.Value, LocalTime!.Value, TimeZone!, UtcOffsetMinutes);
}

public sealed record SchedulePostRequest([Required] Guid? PostVersionId, [Required] DateOnly? LocalDate, [Required] TimeOnly? LocalTime,
    [Required] string? TimeZone, int? UtcOffsetMinutes);

public sealed record PublishNowRequest([Required] Guid? PostVersionId);

/// <summary>
/// Publication intent commands. Schedule and Publish Now only create a durable PublicationJob; no social provider is
/// called from here. 201 means a new intent, 200 means the identical intent already existed (duplicate click).
/// </summary>
[ApiController]
[Route("api/v1/organizations/{orgId:guid}")]
public sealed class PublicationsController(PublicationService publications) : ControllerBase
{
    /// <summary>Admin/Creator. Requires If-Match with the post's ETag.</summary>
    [HttpPost("posts/{postId:guid}/schedule")]
    public async Task<ActionResult<PublicationJobDto>> Schedule(Guid orgId, Guid postId, SchedulePostRequest request, CancellationToken cancellationToken) =>
        Result(await publications.ScheduleAsync(orgId, postId, request.PostVersionId!.Value,
            new LocalSchedule(request.LocalDate!.Value, request.LocalTime!.Value, request.TimeZone!, request.UtcOffsetMinutes),
            RowVersionHeaders.ReadIfMatch(Request), cancellationToken));

    /// <summary>Admin/Creator. Creates an immediately due job for the publishing worker. Requires If-Match with the post's ETag.</summary>
    [HttpPost("posts/{postId:guid}/publish-now")]
    public async Task<ActionResult<PublicationJobDto>> PublishNow(Guid orgId, Guid postId, PublishNowRequest request, CancellationToken cancellationToken) =>
        Result(await publications.ScheduleAsync(orgId, postId, request.PostVersionId!.Value, null, RowVersionHeaders.ReadIfMatch(Request), cancellationToken));

    /// <summary>
    /// Any member. Calendar filters: <c>from</c>/<c>to</c> (UTC, on the publish time), <c>platform</c>, repeated <c>state</c>.
    /// <c>openOnly</c> hides finished (published, simulated, failed, canceled) jobs.
    /// </summary>
    [HttpGet("publication-jobs")]
    public Task<IReadOnlyList<PublicationJobDto>> List(
        Guid orgId, [FromQuery] Guid? postId, [FromQuery] bool openOnly, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] SocialPlatform? platform, [FromQuery(Name = "state")] DeliveryState[]? states, CancellationToken cancellationToken) =>
        publications.ListAsync(orgId, new PublicationQuery(postId, openOnly, ToUtc(from), ToUtc(to), platform, states,
            SoonestFirst: from is not null || to is not null), cancellationToken);

    private static DateTime? ToUtc(DateTime? value) => value is { } v ? (v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : DateTime.SpecifyKind(v, DateTimeKind.Utc)) : null;

    [HttpGet("publication-jobs/{jobId:guid}")]
    public async Task<PublicationJobDto> Get(Guid orgId, Guid jobId, CancellationToken cancellationToken) =>
        WithETag(await publications.GetAsync(orgId, jobId, cancellationToken));

    [HttpGet("publication-jobs/{jobId:guid}/attempts")]
    public Task<IReadOnlyList<PublicationAttemptDto>> Attempts(Guid orgId, Guid jobId, CancellationToken cancellationToken) =>
        publications.ListAttemptsAsync(orgId, jobId, cancellationToken);

    /// <summary>Admin/Creator. Only before dispatch starts. Requires If-Match with the job's ETag.</summary>
    [HttpPost("publication-jobs/{jobId:guid}/cancel")]
    public async Task<PublicationJobDto> Cancel(Guid orgId, Guid jobId, CancellationToken cancellationToken) =>
        WithETag(await publications.CancelAsync(orgId, jobId, RowVersionHeaders.ReadIfMatch(Request), cancellationToken));

    /// <summary>Admin/Creator. Moves a scheduled (or reactivates a canceled) job. Requires If-Match with the job's ETag.</summary>
    [HttpPost("publication-jobs/{jobId:guid}/reschedule")]
    public async Task<PublicationJobDto> Reschedule(Guid orgId, Guid jobId, ScheduleTimeRequest request, CancellationToken cancellationToken) =>
        WithETag(await publications.RescheduleAsync(orgId, jobId, request.ToSchedule(), RowVersionHeaders.ReadIfMatch(Request), cancellationToken));

    private ActionResult<PublicationJobDto> Result(ScheduleResult result)
    {
        WithETag(result.Job);
        return result.Created ? CreatedAtAction(nameof(Get), new { orgId = RouteData.Values["orgId"], jobId = result.Job.Id }, result.Job) : Ok(result.Job);
    }

    private PublicationJobDto WithETag(PublicationJobDto job)
    {
        Response.Headers.ETag = RowVersionHeaders.ToETag(job.RowVersion);
        return job;
    }
}
