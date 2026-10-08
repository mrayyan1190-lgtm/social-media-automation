using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.Audit.Services;
using SocialMediaAutomation.Application.Common;
using SocialMediaAutomation.Application.Common.Authorization;
using SocialMediaAutomation.Application.Media.Services;
using SocialMediaAutomation.Application.Posts.Commands;
using SocialMediaAutomation.Application.Posts.DTOs;
using SocialMediaAutomation.Application.Publications.Services;
using SocialMediaAutomation.Domain.Common;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Application.Posts.Services;

public sealed class PostService(
    OrganizationAuthorizer authorizer,
    IPostRepository posts,
    MediaService mediaService,
    ISocialAccountRepository socialAccounts,
    ActivityRecorder activity,
    PublicationService publications,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    /// <summary>All posts of the workspace (Planner, Home).</summary>
    public async Task<IReadOnlyList<PostListItemDto>> ListAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ViewContent, cancellationToken);
        return await posts.ListAsync(access.OrganizationId, cancellationToken);
    }

    /// <summary>Creates a platform post with Version 1 and the current-version pointer in one transaction.</summary>
    public async Task<PostDto> CreateAsync(CreatePostCommand command, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(command.OrganizationId, Permission.ManageContent, cancellationToken);
        var media = await LoadMediaAsync(access, command.Content.Images, cancellationToken);
        var destination = await LoadDestinationAsync(access, command.Content.SocialAccountId, cancellationToken);
        var (post, version) = Post.Create(
            Guid.NewGuid(), Guid.NewGuid(), access.OrganizationId, command.Platform, command.Content, media, destination,
            PostVersionSource.Manual, access.UserId, Now);
        posts.Add(post);
        posts.Add(version);
        activity.VersionCreated(post, version, access.UserId, revokedApprovalId: null);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(access, post, cancellationToken);
    }

    public async Task<PostDto> GetAsync(Guid organizationId, Guid postId, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ViewContent, cancellationToken);
        return await ToDtoAsync(access, await LoadPostAsync(access, postId, cancellationToken), cancellationToken);
    }

    public async Task<IReadOnlyList<PostVersionDto>> ListVersionsAsync(Guid organizationId, Guid postId, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ViewContent, cancellationToken);
        var post = await LoadPostAsync(access, postId, cancellationToken);
        return await posts.ListVersionsAsync(access.OrganizationId, post.Id, cancellationToken);
    }

    /// <summary>
    /// Saves an edit as a new immutable version (the previous one is never touched). A request that changes nothing
    /// public-facing creates no version and returns the post unchanged.
    /// </summary>
    public async Task<PostDto> ReviseAsync(RevisePostCommand command, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(command.OrganizationId, Permission.ManageContent, cancellationToken);
        var post = await LoadPostAsync(access, command.PostId, cancellationToken);
        ConcurrencyGuard.EnsureCurrent(post.RowVersion, command.ExpectedRowVersion);

        var current = await posts.GetVersionAsync(access.OrganizationId, post.Id, post.CurrentVersionId!.Value, cancellationToken)
            ?? throw new InvalidOperationException($"Post {post.Id} has no current version row.");
        var media = await LoadMediaAsync(access, command.Content.Images, cancellationToken);
        var destination = await LoadDestinationAsync(access, command.Content.SocialAccountId, cancellationToken);

        var approvalBefore = post.ActiveApprovalId;
        var jobsToCancel = await publications.JobsToCancelForEditAsync(post, cancellationToken);
        var next = post.Revise(Guid.NewGuid(), current, command.Content, media, destination, PostVersionSource.Manual, access.UserId, Now);
        if (next is not null)
        {
            posts.Add(next);
            publications.CancelForEdit(jobsToCancel, next.Id, access.UserId);
            activity.VersionCreated(post, next, access.UserId, revokedApprovalId: approvalBefore);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        return await ToDtoAsync(access, post, cancellationToken);
    }

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    private async Task<Post> LoadPostAsync(OrganizationAccess access, Guid postId, CancellationToken cancellationToken)
    {
        var post = await posts.GetAsync(postId, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.NotFound, "Post not found.");
        access.EnsureOwns(post.OrganizationId, "Post");
        return post;
    }

    /// <summary>The requested images in order, each one this workspace's own asset.</summary>
    private async Task<IReadOnlyList<MediaAsset>> LoadMediaAsync(OrganizationAccess access, IReadOnlyList<PostImage>? images, CancellationToken cancellationToken)
    {
        if (images is { Count: > PlatformRules.MaxImages })
            throw Guard.Invalid("Images", $"at most {PlatformRules.MaxImages} images can be attached to one post");
        var media = new List<MediaAsset>();
        foreach (var image in images ?? [])
            media.Add(await mediaService.LoadAsync(access, image.MediaAssetId, cancellationToken));
        return media;
    }

    private async Task<SocialAccount?> LoadDestinationAsync(OrganizationAccess access, Guid? socialAccountId, CancellationToken cancellationToken)
    {
        if (socialAccountId is not { } id)
            return null;
        var account = await socialAccounts.GetAsync(id, cancellationToken) ?? throw new BusinessException(ErrorCodes.NotFound, "Destination account not found.");
        access.EnsureOwns(account.OrganizationId, "Destination account");
        return account;
    }

    private async Task<PostDto> ToDtoAsync(OrganizationAccess access, Post post, CancellationToken cancellationToken)
    {
        var current = (await posts.ListVersionsAsync(access.OrganizationId, post.Id, cancellationToken)).First(v => v.Id == post.CurrentVersionId);
        return new PostDto(post.Id, post.OrganizationId, post.Platform, post.EditorialState, current.Id, post.ActiveApprovalId,
            post.CreatedAtUtc, post.UpdatedAtUtc, post.RowVersion, current);
    }
}
