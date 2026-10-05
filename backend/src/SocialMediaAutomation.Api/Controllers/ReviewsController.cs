using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using SocialMediaAutomation.Api.Extensions;
using SocialMediaAutomation.Application.Reviews.DTOs;
using SocialMediaAutomation.Application.Reviews.Services;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Api.Controllers;

public sealed record SubmitForReviewRequest(string? Note);

public sealed record RequestChangesRequest(string? Explanation);

public sealed record AddReviewCommentRequest([Required] string? Body);

/// <summary>Explicit review commands (ARCHITECTURE.md §40); there is no endpoint that sets a state string directly.</summary>
[ApiController]
[Route("api/v1/organizations/{orgId:guid}")]
public sealed class ReviewsController(ReviewService reviews) : ControllerBase
{
    /// <summary>Submits the post's current version for review. Requires If-Match with the post's ETag.</summary>
    [HttpPost("posts/{postId:guid}/submit-review")]
    public async Task<ActionResult<ReviewSummaryDto>> Submit(Guid orgId, Guid postId, SubmitForReviewRequest? request, CancellationToken cancellationToken)
    {
        var review = WithETag(await reviews.SubmitAsync(orgId, postId, request?.Note, RowVersionHeaders.ReadIfMatch(Request), cancellationToken));
        return CreatedAtAction(nameof(Get), new { orgId, reviewId = review.Id }, review);
    }

    [HttpGet("review-requests")]
    public Task<IReadOnlyList<ReviewSummaryDto>> List(Guid orgId, [FromQuery] ReviewStatus? status, [FromQuery] Guid? postId, CancellationToken cancellationToken) =>
        reviews.ListAsync(orgId, status, postId, cancellationToken);

    [HttpGet("review-requests/{reviewId:guid}")]
    public async Task<ReviewDetailDto> Get(Guid orgId, Guid reviewId, CancellationToken cancellationToken)
    {
        var detail = await reviews.GetDetailAsync(orgId, reviewId, cancellationToken);
        Response.Headers.ETag = RowVersionHeaders.ToETag(detail.Review.RowVersion);
        return detail;
    }

    /// <summary>Reviewer/Admin. Approves exactly the submitted version. Requires If-Match with the review's ETag.</summary>
    [HttpPost("review-requests/{reviewId:guid}/approve")]
    public async Task<ReviewSummaryDto> Approve(Guid orgId, Guid reviewId, CancellationToken cancellationToken) =>
        WithETag(await reviews.ApproveAsync(orgId, reviewId, RowVersionHeaders.ReadIfMatch(Request), cancellationToken));

    /// <summary>Reviewer/Admin. An explanation is mandatory. Requires If-Match with the review's ETag.</summary>
    [HttpPost("review-requests/{reviewId:guid}/request-changes")]
    public async Task<ReviewSummaryDto> RequestChanges(Guid orgId, Guid reviewId, RequestChangesRequest request, CancellationToken cancellationToken) =>
        WithETag(await reviews.RequestChangesAsync(orgId, reviewId, request.Explanation, RowVersionHeaders.ReadIfMatch(Request), cancellationToken));

    [HttpPost("review-requests/{reviewId:guid}/comments")]
    public async Task<ActionResult<ReviewCommentDto>> Comment(Guid orgId, Guid reviewId, AddReviewCommentRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await reviews.AddCommentAsync(orgId, reviewId, request.Body, cancellationToken));

    private ReviewSummaryDto WithETag(ReviewSummaryDto review)
    {
        Response.Headers.ETag = RowVersionHeaders.ToETag(review.RowVersion);
        return review;
    }
}
