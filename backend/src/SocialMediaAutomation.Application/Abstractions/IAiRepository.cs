using SocialMediaAutomation.Application.AiGeneration.DTOs;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Application.Abstractions;

public interface IAiRepository
{
    Task<AiProviderSettings?> GetSettingsAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>Tracked; loads by id only, so callers verify OrganizationId (API) or hold its lease (worker).</summary>
    Task<AiGenerationRequest?> GetRequestAsync(Guid requestId, CancellationToken cancellationToken);

    /// <summary>The pending or processing rewrite/image request for the same post, used to make repeated clicks idempotent.</summary>
    Task<AiGenerationRequest?> FindActiveRequestAsync(Guid organizationId, AiRequestKind kind, Guid postId, CancellationToken cancellationToken);

    /// <summary>
    /// A pending or processing generation by the same user with the identical input snapshot and campaign: a repeated
    /// click returns it instead of writing the same drafts twice. A different prompt or option is a new request.
    /// </summary>
    Task<AiGenerationRequest?> FindActiveGenerationAsync(
        Guid organizationId, Guid requestedByUserId, Guid? campaignId, string inputSnapshot, CancellationToken cancellationToken);


    Task<IReadOnlyList<AiOutputDto>> ListOutputsAsync(Guid organizationId, Guid requestId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ContentEvaluationDto>> ListEvaluationsAsync(Guid organizationId, Guid postId, CancellationToken cancellationToken);

    void Add(AiProviderSettings settings);

    void Add(AiGenerationRequest request);

    void Add(AiRequestOutput output);

    void Add(AiCallLog log);

    void Add(ContentEvaluation evaluation);
}

/// <summary>Durable claiming of AI work (ARCHITECTURE.md §29): one short statement, never held across the LLM call.</summary>
public interface IAiWorkQueue
{
    /// <summary>
    /// Atomically claims the oldest due request (pending and due, or processing with an expired lease), increments its
    /// attempt count and stamps a fresh lease token. Returns null when nothing is due.
    /// </summary>
    Task<(Guid RequestId, Guid LeaseToken)?> TryClaimNextAsync(string workerId, TimeSpan leaseDuration, CancellationToken cancellationToken);
}

/// <summary>Encrypts tenant-supplied secrets (e.g. LLM API keys) at rest.</summary>
public interface ISecretProtector
{
    string Protect(string plaintext);

    string Unprotect(string protectedValue);
}
