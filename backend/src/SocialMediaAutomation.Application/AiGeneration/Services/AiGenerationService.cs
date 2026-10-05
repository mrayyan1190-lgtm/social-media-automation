using System.Text.Json;
using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.AiGeneration.Contracts;
using SocialMediaAutomation.Application.AiGeneration.DTOs;
using SocialMediaAutomation.Application.Common;
using SocialMediaAutomation.Application.Common.Authorization;
using SocialMediaAutomation.Domain.Common;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Application.AiGeneration.Services;

/// <summary>
/// API side of AI work: validates access and inputs, freezes an input snapshot and enqueues a durable request.
/// It never calls the LLM; the AI worker does, after the HTTP request has returned.
/// </summary>
public sealed class AiGenerationService(
    OrganizationAuthorizer authorizer,
    ICorrelationContext correlation,
    IAiRepository ai,
    AiConnectionResolver llmConnections,
    ICampaignRepository campaigns,
    IBrandProfileRepository brandProfiles,
    IOrganizationRepository organizations,
    IPostRepository posts,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    /// <summary>Enqueues generation for the campaign's platforms (or a subset). Repeated clicks return the active request.</summary>
    public async Task<AiGenerationRequestDto> RequestGenerationAsync(
        Guid organizationId, Guid campaignId, IReadOnlyCollection<SocialPlatform>? platforms, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ManageCampaigns, cancellationToken);
        var campaign = await LoadCampaignAsync(access, campaignId, cancellationToken);
        campaign.EnsureEditable();

        if (await ai.FindActiveRequestAsync(access.OrganizationId, campaign.Id, AiRequestKind.Generation, null, cancellationToken) is { } active)
            return await ToDtoAsync(access, active, cancellationToken);

        var brandExists = await brandProfiles.GetByOrganizationAsync(access.OrganizationId, cancellationToken) is not null;
        if (campaign.GenerationGaps(brandExists) is { Count: > 0 } gaps)
            throw new BusinessException(ErrorCodes.CampaignNotReadyForGeneration,
                "The campaign is not ready for generation: " + string.Join(" ", gaps.Select(g => g.Message)));

        var llm = await RequireLlmAsync(access, cancellationToken);
        var targets = platforms is { Count: > 0 } ? platforms.Distinct().ToList() : campaign.Platforms;
        foreach (var platform in targets)
        {
            // Generation replaces drafts only; content under review or approved must be changed deliberately.
            if (await posts.FindActivePostAsync(access.OrganizationId, campaign.Id, platform, cancellationToken) is { } existing
                && existing.EditorialState is not (EditorialState.DRAFT or EditorialState.CHANGES_REQUESTED))
            {
                throw new BusinessException(ErrorCodes.InvalidStateTransition,
                    $"The {platform} post is {existing.EditorialState}; generate only for platforms whose post is a draft.");
            }
        }

        var snapshot = await SnapshotAsync(access, campaign, targets, rewrite: null, cancellationToken);
        var request = AiGenerationRequest.CreateGeneration(
            Guid.NewGuid(), campaign, targets, JsonSerializer.Serialize(snapshot, AiVersions.Json),
            AiVersions.GenerationPrompt, AiVersions.ContentSchema, llm.Provider, llm.Model, access.UserId, Now);
        request.AttachCorrelation(correlation.CorrelationId ?? CorrelationIds.New());
        ai.Add(request);
        campaign.MarkActive(access.UserId, Now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return AiGenerationRequestDto.From(request, []);
    }

    /// <summary>Enqueues a rewrite of the post's current version, which must be the version the user is looking at.</summary>
    public async Task<AiGenerationRequestDto> RequestRewriteAsync(
        Guid organizationId, Guid postId, RewriteAction action, string? customInstruction, byte[]? expectedRowVersion, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ManageCampaigns, cancellationToken);
        var post = await posts.GetAsync(postId, cancellationToken) ?? throw new BusinessException(ErrorCodes.NotFound, "Post not found.");
        access.EnsureOwns(post.OrganizationId, "Post");
        ConcurrencyGuard.EnsureCurrent(post.RowVersion, expectedRowVersion);
        if (post.EditorialState is EditorialState.IN_REVIEW or EditorialState.ARCHIVED)
            throw new BusinessException(ErrorCodes.InvalidStateTransition, $"A post in {post.EditorialState} cannot be rewritten.");

        var campaign = await LoadCampaignAsync(access, post.CampaignId, cancellationToken);
        if (await ai.FindActiveRequestAsync(access.OrganizationId, campaign.Id, AiRequestKind.Rewrite, post.Id, cancellationToken) is { } active)
            return await ToDtoAsync(access, active, cancellationToken);

        var llm = await RequireLlmAsync(access, cancellationToken);
        var source = await posts.GetVersionAsync(access.OrganizationId, post.Id, post.CurrentVersionId!.Value, cancellationToken)
            ?? throw new InvalidOperationException($"Post {post.Id} has no current version row.");
        var rewrite = new RewriteSnapshot(post.Platform, action, customInstruction?.Trim(),
            new SourceContentSnapshot(source.VersionNumber, source.Hook, source.Body, source.CallToAction, source.Hashtags, source.MediaAssetId is not null));
        var snapshot = await SnapshotAsync(access, campaign, [post.Platform], rewrite, cancellationToken);

        var request = AiGenerationRequest.CreateRewrite(
            Guid.NewGuid(), campaign, post, source.Id, action, customInstruction, JsonSerializer.Serialize(snapshot, AiVersions.Json),
            AiVersions.RewritePrompt, AiVersions.ContentSchema, llm.Provider, llm.Model, access.UserId, Now);
        request.AttachCorrelation(correlation.CorrelationId ?? CorrelationIds.New());
        ai.Add(request);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return AiGenerationRequestDto.From(request, []);
    }

    /// <summary>
    /// Enqueues an image for the post's current version (the one the user is looking at). The worker stores it as an
    /// AI-generated MediaAsset and attaches it as a new version, which needs review like any other change.
    /// </summary>
    public async Task<AiGenerationRequestDto> RequestImageAsync(
        Guid organizationId, Guid postId, string? instruction, byte[]? expectedRowVersion, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ManageCampaigns, cancellationToken);
        var post = await posts.GetAsync(postId, cancellationToken) ?? throw new BusinessException(ErrorCodes.NotFound, "Post not found.");
        access.EnsureOwns(post.OrganizationId, "Post");
        ConcurrencyGuard.EnsureCurrent(post.RowVersion, expectedRowVersion);
        if (post.EditorialState is EditorialState.IN_REVIEW or EditorialState.ARCHIVED)
            throw new BusinessException(ErrorCodes.InvalidStateTransition, $"A post in {post.EditorialState} cannot get a new image.");

        var campaign = await LoadCampaignAsync(access, post.CampaignId, cancellationToken);
        if (await ai.FindActiveRequestAsync(access.OrganizationId, campaign.Id, AiRequestKind.ImageGeneration, post.Id, cancellationToken) is { } active)
            return await ToDtoAsync(access, active, cancellationToken);

        var image = await llmConnections.ResolveImageAsync(cancellationToken)
            ?? throw new BusinessException(ErrorCodes.AiNotConfigured, "AI image generation is not available yet. Please contact support.");
        var source = await posts.GetVersionAsync(access.OrganizationId, post.Id, post.CurrentVersionId!.Value, cancellationToken)
            ?? throw new InvalidOperationException($"Post {post.Id} has no current version row.");
        var imageSnapshot = new ImageSnapshot(post.Platform, instruction?.Trim(),
            new SourceContentSnapshot(source.VersionNumber, source.Hook, source.Body, source.CallToAction, source.Hashtags, source.MediaAssetId is not null),
            image.Size);
        var snapshot = await SnapshotAsync(access, campaign, [post.Platform], null, cancellationToken) with { Image = imageSnapshot };

        var request = AiGenerationRequest.CreateImage(Guid.NewGuid(), campaign, post, source.Id, instruction,
            JsonSerializer.Serialize(snapshot, AiVersions.Json), AiVersions.ImagePrompt, image.Provider, image.Model, access.UserId, Now);
        request.AttachCorrelation(correlation.CorrelationId ?? CorrelationIds.New());
        ai.Add(request);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return AiGenerationRequestDto.From(request, []);
    }

    /// <summary>Which AI actions this workspace can use (so the UI shows only working buttons). Any member may ask.</summary>
    public async Task<AiCapabilitiesDto> CapabilitiesAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ViewCampaigns, cancellationToken);
        return new AiCapabilitiesDto(
            await llmConnections.ResolveAsync(access.OrganizationId, cancellationToken) is not null,
            await llmConnections.ResolveImageAsync(cancellationToken) is not null);
    }

    public async Task<AiGenerationRequestDto> GetAsync(Guid organizationId, Guid requestId, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ViewCampaigns, cancellationToken);
        var request = await ai.GetRequestAsync(requestId, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.NotFound, "AI request not found.");
        access.EnsureOwns(request.OrganizationId, "AI request");
        return await ToDtoAsync(access, request, cancellationToken);
    }

    public async Task<IReadOnlyList<AiGenerationRequestDto>> ListForCampaignAsync(Guid organizationId, Guid campaignId, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ViewCampaigns, cancellationToken);
        var campaign = await LoadCampaignAsync(access, campaignId, cancellationToken);
        return await ai.ListRequestsForCampaignAsync(access.OrganizationId, campaign.Id, take: 10, cancellationToken);
    }

    public async Task<IReadOnlyList<ContentEvaluationDto>> ListEvaluationsAsync(Guid organizationId, Guid postId, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ViewCampaigns, cancellationToken);
        var post = await posts.GetAsync(postId, cancellationToken) ?? throw new BusinessException(ErrorCodes.NotFound, "Post not found.");
        access.EnsureOwns(post.OrganizationId, "Post");
        return await ai.ListEvaluationsAsync(access.OrganizationId, post.Id, cancellationToken);
    }

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    private async Task<Campaign> LoadCampaignAsync(OrganizationAccess access, Guid campaignId, CancellationToken cancellationToken)
    {
        var campaign = await campaigns.GetAsync(campaignId, cancellationToken) ?? throw new BusinessException(ErrorCodes.NotFound, "Campaign not found.");
        access.EnsureOwns(campaign.OrganizationId, "Campaign");
        return campaign;
    }

    private async Task<LlmConnection> RequireLlmAsync(OrganizationAccess access, CancellationToken cancellationToken) =>
        await llmConnections.ResolveAsync(access.OrganizationId, cancellationToken)
        ?? throw new BusinessException(ErrorCodes.AiNotConfigured, "AI content generation is not available yet. Please contact support.");

    /// <summary>Freezes every input the model may see: resolved brand, campaign brief, active facts and target platforms.</summary>
    private async Task<AiInputSnapshot> SnapshotAsync(
        OrganizationAccess access, Campaign campaign, IReadOnlyList<SocialPlatform> platforms, RewriteSnapshot? rewrite, CancellationToken cancellationToken)
    {
        var brand = await brandProfiles.GetByOrganizationAsync(access.OrganizationId, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.ValidationFailed, "Complete the brand profile before generating content.");
        var organization = await organizations.GetAsync(access.OrganizationId, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.NotFound, "Organization not found.");
        var facts = await campaigns.ListFactsAsync(access.OrganizationId, campaign.Id, cancellationToken);

        return new AiInputSnapshot(
            AiVersions.Snapshot,
            access.OrganizationId,
            organization.DefaultLanguage,
            new BrandSnapshot(organization.Name, organization.Industry, brand.BusinessDescription, brand.TargetAudience, brand.BrandTone,
                brand.ProhibitedTerms, brand.UpdatedAtUtc),
            new CampaignSnapshot(campaign.Id, campaign.Name, campaign.Objective, campaign.Description, campaign.TargetAudience,
                campaign.CallToAction, CallToActions.Text(campaign.CallToAction), campaign.CallToActionUrl, campaign.ToneOverride ?? brand.BrandTone,
                campaign.ContentLength, campaign.HashtagMode, campaign.EmojiMode),
            facts.Select(f => new FactSnapshot(f.Id, f.Text, f.SourceNote)).ToList(),
            platforms.Order().ToList(),
            rewrite);
    }

    private async Task<AiGenerationRequestDto> ToDtoAsync(OrganizationAccess access, AiGenerationRequest request, CancellationToken cancellationToken) =>
        AiGenerationRequestDto.From(request, await ai.ListOutputsAsync(access.OrganizationId, request.Id, cancellationToken));
}
