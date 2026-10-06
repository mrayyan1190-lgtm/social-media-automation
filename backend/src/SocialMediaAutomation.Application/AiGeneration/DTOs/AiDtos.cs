using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Application.AiGeneration.DTOs;

/// <summary>AI connection as shown to users. The API key itself is never included, only its last four characters.</summary>
public sealed record AiSettingsDto(
    AiProvider Provider,
    string BaseUrl,
    string Model,
    string ApiKeyHint,
    DateTime UpdatedAtUtc,
    byte[] RowVersion)
{
    public static AiSettingsDto From(AiProviderSettings s) => new(s.Provider, s.BaseUrl, s.Model, $"…{s.ApiKeyHint}", s.UpdatedAtUtc, s.RowVersion);
}

public sealed record AiOutputDto(SocialPlatform Platform, Guid PostId, Guid PostVersionId);

public sealed record AiGenerationRequestDto(
    Guid Id,
    Guid? CampaignId,
    AiRequestKind Kind,
    IReadOnlyList<SocialPlatform> Platforms,
    Guid? PostId,
    Guid? SourceVersionId,
    RewriteAction? RewriteAction,
    AiRequestStatus Status,
    int AttemptCount,
    int MaxAttempts,
    string? FailureCode,
    string? FailureMessage,
    AiProvider Provider,
    string Model,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    IReadOnlyList<AiOutputDto> Outputs)
{
    public static AiGenerationRequestDto From(AiGenerationRequest r, IReadOnlyList<AiOutputDto> outputs) => new(
        r.Id, r.CampaignId, r.Kind, r.Platforms, r.PostId, r.SourceVersionId, r.RewriteAction, r.Status, r.AttemptCount, r.MaxAttempts,
        r.FailureCode, r.FailureMessage, r.Provider, r.Model, r.CreatedAtUtc, r.CompletedAtUtc, outputs);
}

public sealed record ContentEvaluationDto(
    Guid PostVersionId,
    int? BrandAlignment,
    int? AudienceRelevance,
    int? Clarity,
    int? PlatformFit,
    int? CtaRelevance,
    string? Summary,
    IReadOnlyList<EvaluationIssue> Issues,
    bool HasCriticalIssue,
    string? EvaluatorModel,
    DateTime CreatedAtUtc);

/// <summary>Which AI actions are available to the workspace right now (configured and enabled by the platform operator).</summary>
public sealed record AiCapabilitiesDto(bool TextGeneration, bool ImageGeneration);
