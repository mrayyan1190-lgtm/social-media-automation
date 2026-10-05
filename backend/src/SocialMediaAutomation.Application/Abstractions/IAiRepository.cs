using SocialMediaAutomation.Application.AiGeneration.DTOs;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Application.Abstractions;

public interface IAiRepository
{
    Task<AiProviderSettings?> GetSettingsAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>Tracked; loads by id only, so callers verify OrganizationId (API) or hold its lease (worker).</summary>
    Task<AiGenerationRequest?> GetRequestAsync(Guid requestId, CancellationToken cancellationToken);

    /// <summary>The pending or processing request for the same target, used to make repeated clicks idempotent.</summary>
    Task<AiGenerationRequest?> FindActiveRequestAsync(Guid organizationId, Guid campaignId, AiRequestKind kind, Guid? postId, CancellationToken cancellationToken);

    Task<IReadOnlyList<AiGenerationRequestDto>> ListRequestsForCampaignAsync(Guid organizationId, Guid campaignId, int take, CancellationToken cancellationToken);

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
