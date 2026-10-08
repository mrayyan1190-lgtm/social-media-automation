using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SocialMediaAutomation.Api.Extensions;
using SocialMediaAutomation.Application.AiGeneration.DTOs;
using SocialMediaAutomation.Application.AiGeneration.Services;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Api.Controllers;

public sealed record SaveAiSettingsRequest([Required] AiProvider? Provider, string? BaseUrl, string Model, string? ApiKey);

public sealed record ListAiModelsRequest([Required] AiProvider? Provider, string? BaseUrl, string? ApiKey);

/// <summary>AI Studio: the prompt and platforms are required; tone defaults to the brand tone and the other options to Medium/Auto.</summary>
public sealed record RequestGenerationRequest(
    string? Prompt, SocialPlatform[]? Platforms, BrandTone? Tone, ContentLength? Length, HashtagMode? Hashtags, EmojiMode? Emojis,
    Guid[]? MediaAssetIds = null, [MaxLength(200)] string? VisionModel = null, bool? WithImage = null, [MaxLength(200)] string? ImageModel = null,
    bool? Unified = null, [Range(1, 4)] int? ImageCount = null);

public sealed record RequestRewriteRequest([Required] RewriteAction? Action, string? CustomInstruction);

/// <summary>Optional visual direction for the images (style only, never a source of facts) and how many to make (1-4, default 1).</summary>
public sealed record RequestImageRequest([MaxLength(500)] string? Instruction, [MaxLength(200)] string? Model = null, [Range(1, 4)] int? Count = null);

[ApiController]
[Route("api/v1/organizations/{orgId:guid}")]
public sealed class AiController(AiSettingsService settings, AiGenerationService generation) : ControllerBase
{
    // ---- Workspace AI provider settings (Admin) ------------------------------------------------------

    /// <summary>Provider, model and a key hint. The API key itself is never returned.</summary>
    [HttpGet("ai-settings")]
    public async Task<AiSettingsDto> GetSettings(Guid orgId, CancellationToken cancellationToken) =>
        WithETag(await settings.GetAsync(orgId, cancellationToken));

    /// <summary>Admin. Creates (no If-Match) or updates (If-Match) the connection. A blank key keeps the stored key.</summary>
    [HttpPut("ai-settings")]
    public async Task<AiSettingsDto> SaveSettings(Guid orgId, SaveAiSettingsRequest request, CancellationToken cancellationToken) =>
        WithETag(await settings.SaveAsync(orgId, request.Provider!.Value, request.BaseUrl, request.Model, request.ApiKey,
            RowVersionHeaders.ReadIfMatch(Request), cancellationToken));

    /// <summary>Admin. POST because the body may carry a key that must not appear in URLs or logs.</summary>
    [EnableRateLimiting(RateLimitingExtensions.Ai)]
    [HttpPost("ai-settings/models")]
    public Task<IReadOnlyList<string>> ListModels(Guid orgId, ListAiModelsRequest request, CancellationToken cancellationToken) =>
        settings.ListModelsAsync(orgId, request.Provider!.Value, request.BaseUrl, request.ApiKey, cancellationToken);

    // ---- Durable generation and rewrites --------------------------------------------------------------

    /// <summary>Enqueues generation and returns 202 immediately; the AI worker writes one draft post per platform. Poll the request.</summary>
    [EnableRateLimiting(RateLimitingExtensions.Ai)]
    [HttpPost("ai-generations")]
    public async Task<ActionResult<AiGenerationRequestDto>> RequestGeneration(Guid orgId, RequestGenerationRequest request, CancellationToken cancellationToken)
    {
        var queued = await generation.RequestGenerationAsync(new RequestGenerationCommand(
            orgId, request.Prompt, request.Platforms, request.Tone, request.Length ?? ContentLength.MEDIUM,
            request.Hashtags ?? HashtagMode.AUTO, request.Emojis ?? EmojiMode.AUTO, request.MediaAssetIds, request.VisionModel,
            request.WithImage ?? false, request.ImageModel, request.Unified ?? false, request.ImageCount ?? 1), cancellationToken);
        return AcceptedAtAction(nameof(GetRequest), new { orgId, requestId = queued.Id }, queued);
    }

    [HttpGet("ai-generations/{requestId:guid}")]
    public Task<AiGenerationRequestDto> GetRequest(Guid orgId, Guid requestId, CancellationToken cancellationToken) =>
        generation.GetAsync(orgId, requestId, cancellationToken);

    /// <summary>Enqueues a rewrite of the current version (If-Match: the post's ETag). The result is a new version.</summary>
    [EnableRateLimiting(RateLimitingExtensions.Ai)]
    [HttpPost("posts/{postId:guid}/rewrites")]
    public async Task<ActionResult<AiGenerationRequestDto>> RequestRewrite(
        Guid orgId, Guid postId, RequestRewriteRequest request, CancellationToken cancellationToken)
    {
        var queued = await generation.RequestRewriteAsync(orgId, postId, request.Action!.Value, request.CustomInstruction,
            RowVersionHeaders.ReadIfMatch(Request), cancellationToken);
        return AcceptedAtAction(nameof(GetRequest), new { orgId, requestId = queued.Id }, queued);
    }

    /// <summary>Enqueues 1-4 AI images for the post (If-Match: the post's ETag). They replace its images in a new version needing review.</summary>
    [EnableRateLimiting(RateLimitingExtensions.Ai)]
    [HttpPost("posts/{postId:guid}/image-generations")]
    public async Task<ActionResult<AiGenerationRequestDto>> RequestImage(Guid orgId, Guid postId, RequestImageRequest? request, CancellationToken cancellationToken)
    {
        var queued = await generation.RequestImageAsync(orgId, postId, request?.Instruction, RowVersionHeaders.ReadIfMatch(Request), cancellationToken,
            request?.Model, request?.Count ?? 1);
        return AcceptedAtAction(nameof(GetRequest), new { orgId, requestId = queued.Id }, queued);
    }

    /// <summary>Which AI actions are available (text, image), so the UI only offers working actions.</summary>
    [HttpGet("ai/capabilities")]
    public Task<AiCapabilitiesDto> Capabilities(Guid orgId, CancellationToken cancellationToken) => generation.CapabilitiesAsync(orgId, cancellationToken);

    /// <summary>Advisory evaluations (scores and issues) for every version of the post.</summary>
    [HttpGet("posts/{postId:guid}/evaluations")]
    public Task<IReadOnlyList<ContentEvaluationDto>> ListEvaluations(Guid orgId, Guid postId, CancellationToken cancellationToken) =>
        generation.ListEvaluationsAsync(orgId, postId, cancellationToken);

    private AiSettingsDto WithETag(AiSettingsDto dto)
    {
        Response.Headers.ETag = RowVersionHeaders.ToETag(dto.RowVersion);
        return dto;
    }
}
