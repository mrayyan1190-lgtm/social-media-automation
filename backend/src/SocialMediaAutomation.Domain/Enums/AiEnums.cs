namespace SocialMediaAutomation.Domain.Enums;

/// <summary>
/// Hosted AI providers. All speak the OpenAI chat-completions protocol; OpenAICompatible takes a custom base URL.
/// HuggingFace (Inference Providers router) also serves text-to-image models through its own image protocol.
/// </summary>
public enum AiProvider
{
    OpenAI,
    Groq,
    OpenAICompatible,
    HuggingFace,
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
    /// <summary>Fresh wording, same message and structure.</summary>
    Rewrite,

    /// <summary>A new alternative take (different hook and angle) on the same message.</summary>
    Regenerate,
    Shorten,
    Expand,
    MoreProfessional,
    ImproveHook,
    LessPromotional,
    Custom,
}

/// <summary>What a PROCESSING request is doing right now (shown to the user as progress).</summary>
public enum AiRequestStage
{
    ANALYZING_IMAGES,
    WRITING,
    GENERATING_IMAGE,
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
