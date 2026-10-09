using Microsoft.AspNetCore.Mvc;
using SocialMediaAutomation.Api.Contracts;
using SocialMediaAutomation.Api.Extensions;
using SocialMediaAutomation.Application.Posts.Commands;
using SocialMediaAutomation.Application.Posts.DTOs;
using SocialMediaAutomation.Application.Posts.Services;

namespace SocialMediaAutomation.Api.Controllers;

[ApiController]
[Route("api/v1/organizations/{orgId:guid}")]
public sealed class PostsController(PostService posts) : ControllerBase
{
    /// <summary>Every non-archived post in the workspace with its latest publication job (Planner, Home).</summary>
    [HttpGet("posts")]
    public Task<IReadOnlyList<PostListItemDto>> List(Guid orgId, CancellationToken cancellationToken) =>
        posts.ListAsync(orgId, cancellationToken);

    /// <summary>Creates a platform post together with its Version 1 (Composer).</summary>
    [HttpPost("posts")]
    public async Task<ActionResult<PostDto>> Create(Guid orgId, CreatePostRequest request, CancellationToken cancellationToken)
    {
        var post = WithETag(await posts.CreateAsync(
            new CreatePostCommand(orgId, request.Platform!.Value, request.ToContent(), request.GroupWithPostId), cancellationToken));
        return CreatedAtAction(nameof(Get), new { orgId, postId = post.Id }, post);
    }

    [HttpGet("posts/{postId:guid}")]
    public async Task<PostDto> Get(Guid orgId, Guid postId, CancellationToken cancellationToken) =>
        WithETag(await posts.GetAsync(orgId, postId, cancellationToken));

    [HttpGet("posts/{postId:guid}/versions")]
    public Task<IReadOnlyList<PostVersionDto>> ListVersions(Guid orgId, Guid postId, CancellationToken cancellationToken) =>
        posts.ListVersionsAsync(orgId, postId, cancellationToken);

    /// <summary>Saves an edit as a new immutable version. Requires If-Match with the post's ETag.</summary>
    [HttpPost("posts/{postId:guid}/versions")]
    public async Task<PostDto> Revise(Guid orgId, Guid postId, SavePostContentRequest request, CancellationToken cancellationToken) =>
        WithETag(await posts.ReviseAsync(
            new RevisePostCommand(orgId, postId, request.ToContent(), RowVersionHeaders.ReadIfMatch(Request)), cancellationToken));

    /// <summary>"Delete draft": archives a DRAFT or CHANGES_REQUESTED post. Requires If-Match with the post's ETag.</summary>
    [HttpPost("posts/{postId:guid}/archive")]
    public async Task<IActionResult> Archive(Guid orgId, Guid postId, CancellationToken cancellationToken)
    {
        await posts.ArchiveAsync(orgId, postId, RowVersionHeaders.ReadIfMatch(Request), cancellationToken);
        return NoContent();
    }

    private PostDto WithETag(PostDto post)
    {
        Response.Headers.ETag = RowVersionHeaders.ToETag(post.RowVersion);
        return post;
    }
}
