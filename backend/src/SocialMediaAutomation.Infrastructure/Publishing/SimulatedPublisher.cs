using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.Publications.Services;
using SocialMediaAutomation.Domain.Common;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Infrastructure.Publishing;

/// <summary>
/// The explicit simulation (PRD §23). It runs through the same worker, revalidation and attempt history as a real
/// publisher, never calls a provider and returns no external id or URL, so the job ends SIMULATED, never PUBLISHED.
/// The receipt describes deterministically what would have been sent.
/// </summary>
public sealed class SimulatedPublisher : ISocialPublisher
{
    public bool CanPublish(SocialPlatform platform, ExecutionMode mode) => mode == ExecutionMode.SIMULATED;

    public bool UsesMediaContainer => false;

    public Task ValidateCapabilityAsync(PublishContext context, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<string> PrepareAsync(PublishContext context, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The simulation has no preparation step.");

    public Task<MediaContainerStatus> GetStatusAsync(PublishContext context, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The simulation has no media container.");

    public Task<PublishReceipt> PublishAsync(PublishContext context, CancellationToken cancellationToken)
    {
        var text = PublicationText.Compose(context.Payload, includeUrl: true);
        var image = context.Media is null ? "no image" : $"1 image ({context.Media.ContentType}, sha256 {context.Media.Sha256[..12]})";
        var detail = $"Simulated {context.Platform} publication: {text.Length} characters, {context.Payload.Hashtags.Count} hashtag(s), {image}, " +
            $"payload {context.Payload.ComputeHash()[..12]}. Nothing was sent to {context.Platform}.";
        return Task.FromResult(new PublishReceipt(null, null, null, detail));
    }

    public PublishFailure NormalizeError(Exception error, bool mayHaveCreatedPost) =>
        error as PublishFailure ?? new PublishFailure(Domain.Entities.PublishErrorCategory.RETRYABLE, "SIMULATION_ERROR", error.Message);
}
