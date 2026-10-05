using System.Text;
using SocialMediaAutomation.Domain.Common;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Application.Abstractions;

/// <summary>The approved image as the publisher sees it: stable identity plus a way to read the bytes at dispatch time.</summary>
public sealed record PublishMedia(Guid AssetId, string ContentType, string Sha256, string? AltText, Func<CancellationToken, Task<Stream>> OpenAsync,
    string? PublicUrl);

/// <summary>
/// Everything one publisher needs for one revalidated job. The payload is the frozen approved payload. The access token
/// is decrypted only for this call; ToString is redacted so it cannot reach a log.
/// </summary>
public sealed record PublishContext(
    Guid JobId,
    SocialPlatform Platform,
    ExecutionMode ExecutionMode,
    string? ExternalAccountId,
    IReadOnlyList<string> Capabilities,
    ApprovalPayload Payload,
    PublishMedia? Media,
    string? AccessToken,
    string? ContainerId)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"JobId = {JobId}, Platform = {Platform}, ExecutionMode = {ExecutionMode}, ExternalAccountId = {ExternalAccountId}, ");
        builder.Append($"HasMedia = {Media is not null}, ContainerId = {ContainerId}, AccessToken = ***");
        return true;
    }
}

/// <summary>Provider evidence of a completed publication. A simulated receipt carries no external id or URL.</summary>
public sealed record PublishReceipt(string? ExternalId, string? ExternalUrl, string? ProviderRequestId, string Detail);

public enum MediaContainerStatus
{
    InProgress,
    Ready,
    Failed,
}

/// <summary>A normalized provider failure; <see cref="Category"/> alone decides retry, action or reconciliation.</summary>
public sealed class PublishFailure(PublishErrorCategory category, string code, string message, string? providerRequestId = null) : Exception(message)
{
    public PublishErrorCategory Category { get; } = category;
    public string Code { get; } = code;
    public string? ProviderRequestId { get; } = providerRequestId;
}

/// <summary>
/// One provider (or the simulation) behind a common contract (ARCHITECTURE.md §31). Provider-specific payload mapping,
/// multi-step flows and error normalization stay inside the implementation. Every method throws only
/// <see cref="PublishFailure"/>; anything else escaping <see cref="PublishAsync"/> is treated as an uncertain outcome.
/// </summary>
public interface ISocialPublisher
{
    /// <summary>Selection: which platform and execution mode this publisher serves. SIMULATED is served only by the simulation.</summary>
    bool CanPublish(SocialPlatform platform, ExecutionMode mode);

    /// <summary>True when publishing needs a provider-side media container that is created, polled and then published.</summary>
    bool UsesMediaContainer { get; }

    /// <summary>Synchronous checks on granted capabilities, destination and media; throws ACTION_REQUIRED failures.</summary>
    Task ValidateCapabilityAsync(PublishContext context, CancellationToken cancellationToken);

    /// <summary>Creates the media container (container publishers only). Creating one never makes anything visible.</summary>
    Task<string> PrepareAsync(PublishContext context, CancellationToken cancellationToken);

    /// <summary>Processing state of <see cref="PublishContext.ContainerId"/> (container publishers only).</summary>
    Task<MediaContainerStatus> GetStatusAsync(PublishContext context, CancellationToken cancellationToken);

    /// <summary>The publishing call. Called only after the dispatch checkpoint is saved.</summary>
    Task<PublishReceipt> PublishAsync(PublishContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Maps a raw error to a category. <paramref name="mayHaveCreatedPost"/> is true when the failed request could have
    /// created the post, which turns ambiguous errors (timeouts, 5xx) into UNCERTAIN instead of RETRYABLE.
    /// </summary>
    PublishFailure NormalizeError(Exception error, bool mayHaveCreatedPost);
}

/// <summary>Durable claiming of due publication jobs. Claims are single auto-committed statements; no lock outlives them.</summary>
public interface IPublicationWorkQueue
{
    Task<IReadOnlyList<(Guid JobId, Guid LeaseToken)>> ClaimDueAsync(string workerId, int batchSize, TimeSpan leaseDuration, CancellationToken cancellationToken);
}
