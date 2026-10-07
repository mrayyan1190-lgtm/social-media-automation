using SocialMediaAutomation.Domain.Common;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Domain.Entities;

/// <summary>
/// Durable AI work item (ARCHITECTURE.md §16). Created by the API with a frozen input snapshot and processed later by
/// the AI worker. Workers claim it with a lease; every state change after the claim must present the lease token
/// (fencing), so a worker whose lease expired cannot overwrite newer state.
/// </summary>
public sealed class AiGenerationRequest
{
    public const int DefaultMaxAttempts = 3;
    public const int CustomInstructionMax = 500;
    public const int WarningMax = 500;

    private AiGenerationRequest() { }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    /// <summary>Generation: the campaign folder the new posts go into. Rewrite/image: the post's folder at request time.</summary>
    public Guid? CampaignId { get; private set; }
    public AiRequestKind Kind { get; private set; }
    public List<SocialPlatform> Platforms { get; private set; } = [];

    /// <summary>Rewrite only: the post and the exact version the rewrite starts from.</summary>
    public Guid? PostId { get; private set; }
    public Guid? SourceVersionId { get; private set; }
    public RewriteAction? RewriteAction { get; private set; }
    public string? CustomInstruction { get; private set; }

    /// <summary>Frozen, versioned JSON of every input the model sees. Later brand/campaign edits do not change it.</summary>
    public string InputSnapshot { get; private set; } = null!;
    public string PromptVersion { get; private set; } = null!;
    public string SchemaVersion { get; private set; } = null!;
    public AiProvider Provider { get; private set; }
    public string Model { get; private set; } = null!;

    public AiRequestStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public int MaxAttempts { get; private set; }
    public DateTime NextAttemptAtUtc { get; private set; }
    public Guid? LeaseToken { get; private set; }
    public string? LeasedBy { get; private set; }
    public DateTime? LeaseExpiresAtUtc { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }
    /// <summary>Progress while PROCESSING; cleared when the attempt ends.</summary>
    public AiRequestStage? Stage { get; private set; }
    /// <summary>A completed request whose optional part did not work (e.g. the drafts were written but the image failed).</summary>
    public string? Warning { get; private set; }

    public Guid RequestedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? StartedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    /// <summary>Traces the request that queued this work across API, worker, provider calls and logs.</summary>
    public string? CorrelationId { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public bool IsActive => Status is AiRequestStatus.PENDING or AiRequestStatus.PROCESSING;

    public void AttachCorrelation(string? correlationId) => CorrelationId ??= CorrelationIds.Normalize(correlationId);

    /// <summary>Prompt-based generation: one new draft post per platform, filed into <paramref name="campaign"/> when given.</summary>
    public static AiGenerationRequest CreateGeneration(
        Guid id, Guid organizationId, Campaign? campaign, IReadOnlyCollection<SocialPlatform> platforms, string inputSnapshot, string promptVersion,
        string schemaVersion, AiProvider provider, string model, Guid userId, DateTime utcNow)
    {
        if (platforms.Count == 0)
            throw Guard.Invalid("Platforms", "must include at least one of LinkedIn, Facebook, Instagram");
        foreach (var platform in platforms)
            Guard.Defined(platform, "Platforms");
        if (campaign is not null)
        {
            if (campaign.OrganizationId != organizationId)
                throw new BusinessException(ErrorCodes.TenantMismatch, "The campaign belongs to another workspace.");
            campaign.EnsureEditable();
        }
        return New(id, organizationId, campaign?.Id, AiRequestKind.Generation, platforms, inputSnapshot, promptVersion, schemaVersion, provider, model, userId, utcNow);
    }

    /// <summary>
    /// An image for one post. The optional instruction is a visual style request, never a source of facts. The result is
    /// a new version of the post (from the version the user was looking at), not an edit of a reviewed version.
    /// </summary>
    public static AiGenerationRequest CreateImage(
        Guid id, Post post, Guid sourceVersionId, string? instruction, string inputSnapshot, string promptVersion,
        AiProvider provider, string model, Guid userId, DateTime utcNow)
    {
        var request = New(id, post.OrganizationId, post.CampaignId, AiRequestKind.ImageGeneration, [post.Platform], inputSnapshot, promptVersion,
            "image-v1", provider, model, userId, utcNow);
        request.PostId = post.Id;
        request.SourceVersionId = sourceVersionId;
        request.CustomInstruction = Guard.Optional(instruction, "Image instruction", CustomInstructionMax);
        return request;
    }

    public static AiGenerationRequest CreateRewrite(
        Guid id, Post post, Guid sourceVersionId, RewriteAction action, string? customInstruction, string inputSnapshot,
        string promptVersion, string schemaVersion, AiProvider provider, string model, Guid userId, DateTime utcNow)
    {
        var instruction = Guard.Optional(customInstruction, "Custom instruction", CustomInstructionMax);
        if (Guard.Defined(action, "Rewrite action") == Enums.RewriteAction.Custom && instruction is null)
            throw Guard.Invalid("Custom instruction", "is required for a custom rewrite");

        var request = New(id, post.OrganizationId, post.CampaignId, AiRequestKind.Rewrite, [post.Platform], inputSnapshot, promptVersion,
            schemaVersion, provider, model, userId, utcNow);
        request.PostId = post.Id;
        request.SourceVersionId = sourceVersionId;
        request.RewriteAction = action;
        request.CustomInstruction = action == Enums.RewriteAction.Custom ? instruction : null;
        return request;
    }

    public void ReportStage(Guid leaseToken, AiRequestStage stage)
    {
        EnsureLease(leaseToken);
        Stage = Guard.Defined(stage, "Stage");
    }

    public void Complete(Guid leaseToken, DateTime utcNow, string? warning = null)
    {
        EnsureLease(leaseToken);
        Status = AiRequestStatus.COMPLETED;
        CompletedAtUtc = utcNow;
        Stage = null;
        Warning = warning is { Length: > WarningMax } ? warning[..WarningMax] : warning;
        ReleaseLease();
    }

    /// <summary>Records a failure. Retryable failures go back to PENDING with backoff until attempts run out.</summary>
    public void Fail(Guid leaseToken, string code, string message, bool retryable, DateTime utcNow)
    {
        EnsureLease(leaseToken);
        FailureCode = code;
        FailureMessage = message.Length > 1000 ? message[..1000] : message;
        Stage = null;
        if (retryable && AttemptCount < MaxAttempts)
        {
            // AI calls have no external side effects, so retrying a failed generation is always safe.
            Status = AiRequestStatus.PENDING;
            NextAttemptAtUtc = utcNow.AddSeconds(30 * Math.Pow(2, AttemptCount - 1));
        }
        else
        {
            Status = AiRequestStatus.FAILED;
            CompletedAtUtc = utcNow;
        }
        ReleaseLease();
    }

    public void EnsureLease(Guid leaseToken)
    {
        if (Status != AiRequestStatus.PROCESSING || LeaseToken != leaseToken)
            throw new BusinessException(ErrorCodes.ConcurrencyConflict, "The AI request lease is no longer held by this worker.");
    }

    private void ReleaseLease()
    {
        LeaseToken = null;
        LeasedBy = null;
        LeaseExpiresAtUtc = null;
    }

    private static AiGenerationRequest New(
        Guid id, Guid organizationId, Guid? campaignId, AiRequestKind kind, IReadOnlyCollection<SocialPlatform> platforms, string inputSnapshot,
        string promptVersion, string schemaVersion, AiProvider provider, string model, Guid userId, DateTime utcNow) =>
        new()
        {
            Id = id,
            OrganizationId = organizationId,
            CampaignId = campaignId,
            Kind = kind,
            Platforms = platforms.Distinct().Order().ToList(),
            InputSnapshot = inputSnapshot,
            PromptVersion = promptVersion,
            SchemaVersion = schemaVersion,
            Provider = Guard.Defined(provider, "Provider"),
            Model = Guard.Required(model, "Model", AiProviderSettings.ModelMax),
            Status = AiRequestStatus.PENDING,
            MaxAttempts = DefaultMaxAttempts,
            NextAttemptAtUtc = utcNow,
            RequestedByUserId = userId,
            CreatedAtUtc = utcNow,
        };
}
