using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.Audit.Services;
using SocialMediaAutomation.Application.Common;
using SocialMediaAutomation.Application.Common.Authorization;
using SocialMediaAutomation.Application.Media.Services;
using SocialMediaAutomation.Application.Posts.Commands;
using SocialMediaAutomation.Application.Posts.DTOs;
using SocialMediaAutomation.Application.Publications.Services;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Application.Posts.Services;

public sealed class PostService(
    OrganizationAuthorizer authorizer,
    IPostRepository posts,
    ICampaignRepository campaigns,
    MediaService mediaService,
    ISocialAccountRepository socialAccounts,
    ActivityRecorder activity,
    PublicationService publications,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    /// <summary>All posts of the workspace (Planner, Home), or of one campaign folder.</summary>
    public async Task<IReadOnlyList<PostListItemDto>> ListAsync(Guid organizationId, Guid? campaignId, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ViewCampaigns, cancellationToken);
        if (campaignId is { } id)
            await LoadCampaignAsync(access, id, cancellationToken);
        return await posts.ListAsync(access.OrganizationId, campaignId, cancellationToken);
    }

    /// <summary>Creates a platform post with Version 1 and the current-version pointer in one transaction.</summary>
    public async Task<PostDto> CreateAsync(CreatePostCommand command, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(command.OrganizationId, Permission.ManageCampaigns, cancellationToken);
        var campaign = command.CampaignId is { } campaignId ? await LoadCampaignAsync(access, campaignId, cancellationToken) : null;
        var media = await LoadMediaAsync(access, command.Content.MediaAssetId, cancellationToken);
        var destination = await LoadDestinationAsync(access, command.Content.SocialAccountId, cancellationToken);
        var (post, version) = Post.Create(
            Guid.NewGuid(), Guid.NewGuid(), access.OrganizationId, campaign, command.Platform, command.Content, media, destination,
            PostVersionSource.Manual, access.UserId, Now);
        posts.Add(post);
        posts.Add(version);
        activity.VersionCreated(post, version, access.UserId, revokedApprovalId: null);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(access, post, cancellationToken);
    }

    public async Task<PostDto> GetAsync(Guid organizationId, Guid postId, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ViewCampaigns, cancellationToken);
        return await ToDtoAsync(access, await LoadPostAsync(access, postId, cancellationToken), cancellationToken);
    }

    public async Task<IReadOnlyList<PostVersionDto>> ListVersionsAsync(Guid organizationId, Guid postId, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ViewCampaigns, cancellationToken);
        var post = await LoadPostAsync(access, postId, cancellationToken);
        return await posts.ListVersionsAsync(access.OrganizationId, post.Id, cancellationToken);
    }

    /// <summary>
    /// Saves an edit as a new immutable version (the previous one is never touched). A request that changes nothing
    /// public-facing creates no version and returns the post unchanged.
    /// </summary>
    public async Task<PostDto> ReviseAsync(RevisePostCommand command, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(command.OrganizationId, Permission.ManageCampaigns, cancellationToken);
        var post = await LoadPostAsync(access, command.PostId, cancellationToken);
        ConcurrencyGuard.EnsureCurrent(post.RowVersion, command.ExpectedRowVersion);

        var current = await posts.GetVersionAsync(access.OrganizationId, post.Id, post.CurrentVersionId!.Value, cancellationToken)
            ?? throw new InvalidOperationException($"Post {post.Id} has no current version row.");
        var media = await LoadMediaAsync(access, command.Content.MediaAssetId, cancellationToken);
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

    /// <summary>Moves the post between campaign folders. Requires If-Match with the post's ETag; content and approval are untouched.</summary>
    public async Task<PostDto> MoveToCampaignAsync(MovePostToCampaignCommand command, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(command.OrganizationId, Permission.ManageCampaigns, cancellationToken);
        var post = await LoadPostAsync(access, command.PostId, cancellationToken);
        ConcurrencyGuard.EnsureCurrent(post.RowVersion, command.ExpectedRowVersion);
        var campaign = command.CampaignId is { } campaignId ? await LoadCampaignAsync(access, campaignId, cancellationToken) : null;
        if (post.CampaignId == campaign?.Id)
            return await ToDtoAsync(access, post, cancellationToken);

        var from = post.CampaignId;
        post.MoveToCampaign(campaign, Now);
        activity.Audit(access.OrganizationId, access.UserId, AuditActions.PostCampaignChanged, ActivityRecorder.PostResource, post.Id,
            new Dictionary<string, string?> { ["fromCampaignId"] = from?.ToString(), ["toCampaignId"] = campaign?.Id.ToString() });
        await unitOfWork.SaveChangesAsync(cancellationToken);
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

    private async Task<Campaign> LoadCampaignAsync(OrganizationAccess access, Guid campaignId, CancellationToken cancellationToken)
    {
        var campaign = await campaigns.GetAsync(campaignId, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.NotFound, "Campaign not found.");
        access.EnsureOwns(campaign.OrganizationId, "Campaign");
        return campaign;
    }

    private async Task<MediaAsset?> LoadMediaAsync(OrganizationAccess access, Guid? mediaAssetId, CancellationToken cancellationToken) =>
        mediaAssetId is { } id ? await mediaService.LoadAsync(access, id, cancellationToken) : null;

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
        return new PostDto(post.Id, post.OrganizationId, post.CampaignId, post.Platform, post.EditorialState, current.Id, post.ActiveApprovalId,
            post.CreatedAtUtc, post.UpdatedAtUtc, post.RowVersion, current);
    }
}
