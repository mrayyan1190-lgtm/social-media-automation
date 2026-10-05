using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Domain.Entities;

/// <summary>The validated model output for one platform of one AI request, and the version it produced.</summary>
public sealed class AiRequestOutput
{
    private AiRequestOutput() { }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid RequestId { get; private set; }
    public SocialPlatform Platform { get; private set; }
    public Guid PostId { get; private set; }
    public Guid PostVersionId { get; private set; }
    public string OutputJson { get; private set; } = null!;
    public DateTime CreatedAtUtc { get; private set; }

    public static AiRequestOutput Create(AiGenerationRequest request, PostVersion version, SocialPlatform platform, string outputJson, DateTime utcNow) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = request.OrganizationId,
        RequestId = request.Id,
        Platform = platform,
        PostId = version.PostId,
        PostVersionId = version.Id,
        OutputJson = outputJson,
        CreatedAtUtc = utcNow,
    };
}

/// <summary>
/// One LLM call: what was asked of which model, and how it went. Never contains API keys or prompt secrets;
/// Diagnostic holds at most a truncated provider error or invalid output for debugging.
/// </summary>
public sealed class AiCallLog
{
    public const int DiagnosticMax = 4000;

    private AiCallLog() { }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid RequestId { get; private set; }
    public AiCallPurpose Purpose { get; private set; }
    public AiProvider Provider { get; private set; }
    public string Model { get; private set; } = null!;
    public string PromptVersion { get; private set; } = null!;
    public bool Succeeded { get; private set; }
    public string? ErrorCode { get; private set; }
    public int? HttpStatus { get; private set; }
    public int? InputTokens { get; private set; }
    public int? OutputTokens { get; private set; }
    public int LatencyMs { get; private set; }
    public decimal? EstimatedCostUsd { get; private set; }
    public string? Diagnostic { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static AiCallLog Create(
        AiGenerationRequest request, AiCallPurpose purpose, string promptVersion, bool succeeded, string? errorCode, int? httpStatus,
        int? inputTokens, int? outputTokens, int latencyMs, decimal? estimatedCostUsd, string? diagnostic, DateTime utcNow) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = request.OrganizationId,
        RequestId = request.Id,
        Purpose = purpose,
        Provider = request.Provider,
        Model = request.Model,
        PromptVersion = promptVersion,
        Succeeded = succeeded,
        ErrorCode = errorCode,
        HttpStatus = httpStatus,
        InputTokens = inputTokens,
        OutputTokens = outputTokens,
        LatencyMs = latencyMs,
        EstimatedCostUsd = estimatedCostUsd,
        Diagnostic = diagnostic is { Length: > DiagnosticMax } ? diagnostic[..DiagnosticMax] : diagnostic,
        CreatedAtUtc = utcNow,
    };
}

/// <summary>A deterministic or AI-raised finding about one version. CRITICAL findings are meant to block review submission.</summary>
public sealed record EvaluationIssue(IssueSeverity Severity, string Category, string Message, IssueSource Source, string? Excerpt = null);

/// <summary>
/// Advisory quality assessment of one exact PostVersion (PRD §14). Scores (1-5) come from the AI evaluator and never
/// approve or reject anything; deterministic issues are recorded alongside AI issues with their source.
/// </summary>
public sealed class ContentEvaluation
{
    private ContentEvaluation() { }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid PostId { get; private set; }
    public Guid PostVersionId { get; private set; }
    public Guid? RequestId { get; private set; }
    public int? BrandAlignment { get; private set; }
    public int? AudienceRelevance { get; private set; }
    public int? Clarity { get; private set; }
    public int? PlatformFit { get; private set; }
    public int? CtaRelevance { get; private set; }
    public string? Summary { get; private set; }
    public List<EvaluationIssue> Issues { get; private set; } = [];
    public bool HasCriticalIssue { get; private set; }
    public string? EvaluatorModel { get; private set; }
    public string? PromptVersion { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static ContentEvaluation Create(
        PostVersion version, AiGenerationRequest? request, EvaluationScores? scores, string? summary, IEnumerable<EvaluationIssue> issues,
        string? evaluatorModel, string? promptVersion, DateTime utcNow)
    {
        var list = issues.ToList();
        return new ContentEvaluation
        {
            Id = Guid.NewGuid(),
            OrganizationId = version.OrganizationId,
            PostId = version.PostId,
            PostVersionId = version.Id,
            RequestId = request?.Id,
            BrandAlignment = Clamp(scores?.BrandAlignment),
            AudienceRelevance = Clamp(scores?.AudienceRelevance),
            Clarity = Clamp(scores?.Clarity),
            PlatformFit = Clamp(scores?.PlatformFit),
            CtaRelevance = Clamp(scores?.CtaRelevance),
            Summary = summary is { Length: > 1000 } ? summary[..1000] : summary,
            Issues = list,
            HasCriticalIssue = list.Any(i => i.Severity == IssueSeverity.CRITICAL),
            EvaluatorModel = evaluatorModel,
            PromptVersion = promptVersion,
            CreatedAtUtc = utcNow,
        };
    }

    private static int? Clamp(int? score) => score is null ? null : Math.Clamp(score.Value, 1, 5);
}

public sealed record EvaluationScores(int? BrandAlignment, int? AudienceRelevance, int? Clarity, int? PlatformFit, int? CtaRelevance);
