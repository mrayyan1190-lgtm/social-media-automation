using SocialMediaAutomation.Application.Publications.DTOs;
using SocialMediaAutomation.Domain.Entities;

namespace SocialMediaAutomation.Application.Abstractions;

public interface IPublicationRepository
{
    void Add(PublicationJob job);

    /// <summary>Tracked; loads by id only, so callers verify OrganizationId.</summary>
    Task<PublicationJob?> GetAsync(Guid jobId, CancellationToken cancellationToken);

    /// <summary>Tracked; the lifetime intent for this exact version and destination, if any.</summary>
    Task<PublicationJob?> FindIntentAsync(Guid organizationId, Guid postVersionId, Guid socialAccountId, CancellationToken cancellationToken);

    /// <summary>Tracked; every job of one version (used when the version stops being current).</summary>
    Task<IReadOnlyList<PublicationJob>> ListForVersionAsync(Guid organizationId, Guid postVersionId, CancellationToken cancellationToken);

    void Add(PublicationAttempt attempt);

    /// <summary>Tracked; PROCESSING jobs whose lease expired (input for the reconcile/fail sweep).</summary>
    Task<IReadOnlyList<PublicationJob>> ListExpiredLeasesAsync(DateTime utcNow, int take, CancellationToken cancellationToken);

    /// <summary>Tracked; the latest unfinished attempt of a job, if any.</summary>
    Task<PublicationAttempt?> FindOpenAttemptAsync(Guid jobId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PublicationAttemptDto>> ListAttemptsAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken);

    /// <summary>Newest publish time first unless <see cref="PublicationQuery.SoonestFirst"/>.</summary>
    Task<IReadOnlyList<PublicationJobDto>> ListAsync(Guid organizationId, PublicationQuery query, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<DeliveryState, int>> CountByDeliveryStateAsync(Guid organizationId, CancellationToken cancellationToken);
}
