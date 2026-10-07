using SocialMediaAutomation.Domain.Constants;
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
    IReadOnlyList<AiOutputDto> Outputs,
    AiRequestStage? Stage,
    string? Warning)
{
    public static AiGenerationRequestDto From(AiGenerationRequest r, IReadOnlyList<AiOutputDto> outputs) => new(
        r.Id, r.CampaignId, r.Kind, r.Platforms, r.PostId, r.SourceVersionId, r.RewriteAction, r.Status, r.AttemptCount, r.MaxAttempts,
        r.FailureCode, CustomerFailureMessage(r.FailureCode, r.FailureMessage), r.Provider, r.Model, r.CreatedAtUtc, r.CompletedAtUtc,
        outputs, r.Stage, r.Warning);

    /// <summary>
    /// Workspace users see a plain sentence for provider-side failures; the provider's own text (HTTP status, billing,
    /// model errors) stays in the stored request and the AI call log, which only system administrators see.
    /// Messages the application writes itself (e.g. "An uploaded image is no longer available") are kept.
    /// </summary>
    private static string? CustomerFailureMessage(string? code, string? message) => code switch
    {
        AiFailureCodes.ProviderAuth or AiFailureCodes.NotConfigured => "AI is not available right now. Please contact your administrator.",
        AiFailureCodes.ProviderRejected => "The AI service could not complete this request. Please try again later.",
        AiFailureCodes.ProviderUnavailable => "The AI service is busy or unavailable. Please try again in a moment.",
        AiFailureCodes.OutputInvalid => "The AI returned a result that could not be used. Please try again.",
        AiFailureCodes.WorkerLost => "The request was interrupted. Please try again.",
        AiFailureCodes.Unexpected => "Something went wrong while writing the posts. Please try again.",
        _ => message,
    };
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
/// <summary>ImageModels / VisionModels: the Hugging Face models a user may pick (empty when that choice is not available).</summary>
public sealed record AiCapabilitiesDto(
    bool TextGeneration, bool ImageGeneration, bool ImageAnalysis, IReadOnlyList<string> ImageModels, IReadOnlyList<string> VisionModels);
