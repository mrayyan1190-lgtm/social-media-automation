namespace SocialMediaAutomation.Domain.Enums;

/// <summary>Hosted LLM providers. All speak the OpenAI chat-completions protocol; OpenAICompatible takes a custom base URL.</summary>
public enum AiProvider
{
    OpenAI,
    Groq,
    OpenAICompatible,
}

public enum AiRequestKind
{
    Generation,
    Rewrite,

    /// <summary>An image for one post: stored as a MediaAsset and attached as a new PostVersion that still needs review.</summary>
    ImageGeneration,
}

/// <summary>Durable AI work state. Spelled like the other workflow states (one canonical form in DB, API and UI).</summary>
public enum AiRequestStatus
{
    PENDING,
    PROCESSING,
    COMPLETED,
    FAILED,
}

/// <summary>User-requested rewrite actions (PRD §16). Every rewrite produces a new PostVersion.</summary>
public enum RewriteAction
{
    Shorten,
    Expand,
    MoreProfessional,
    ImproveHook,
    LessPromotional,
    Custom,
}

public enum AiCallPurpose
{
    Generation,
    Evaluation,
    Rewrite,
    ModelListing,
    ImageGeneration,
}

public enum IssueSeverity
{
    INFO,
    WARNING,
    CRITICAL,
}

public enum IssueSource
{
    DETERMINISTIC,
    AI,
}
