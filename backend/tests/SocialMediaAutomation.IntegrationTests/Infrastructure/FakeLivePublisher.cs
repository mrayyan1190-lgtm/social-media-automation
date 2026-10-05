using System.Collections.Concurrent;
using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.IntegrationTests.Infrastructure;

/// <summary>Scripted provider behaviour shared by the fake live publishers. Tests set the delegates; every call is recorded.</summary>
public sealed class PublisherScript
{
    public Func<PublishContext, Task<PublishReceipt>> Publish { get; set; } = Success;

    public Func<PublishContext, string> Prepare { get; set; } = context => $"container-{context.JobId:N}";

    public Func<PublishContext, MediaContainerStatus> Status { get; set; } = _ => MediaContainerStatus.Ready;

    /// <summary>Runs during capability validation, i.e. after the claim and before the dispatch checkpoint.</summary>
    public Func<PublishContext, Task>? OnValidate { get; set; }

    public ConcurrentQueue<(string Step, PublishContext Context)> Calls { get; } = new();

    public int CallsFor(Guid jobId, string step) => Calls.Count(c => c.Context.JobId == jobId && c.Step == step);

    public void Reset()
    {
        Publish = Success;
        Prepare = context => $"container-{context.JobId:N}";
        Status = _ => MediaContainerStatus.Ready;
        OnValidate = null;
    }

    public static Task<PublishReceipt> Success(PublishContext context) =>
        Task.FromResult(new PublishReceipt($"ext-{context.JobId:N}", $"https://provider.test/posts/{context.JobId:N}", "req-123", "fake publish"));
}

/// <summary>Stands in for a live provider adapter (LIVE/TEST_ACCOUNT); the real adapters are covered against stub HTTP.</summary>
public sealed class FakeLivePublisher(PublisherScript script, bool usesMediaContainer, params SocialPlatform[] platforms) : ISocialPublisher
{
    public bool CanPublish(SocialPlatform platform, ExecutionMode mode) =>
        platforms.Contains(platform) && mode is ExecutionMode.LIVE or ExecutionMode.TEST_ACCOUNT;

    public bool UsesMediaContainer => usesMediaContainer;

    public Task ValidateCapabilityAsync(PublishContext context, CancellationToken cancellationToken)
    {
        script.Calls.Enqueue(("validate", context));
        return script.OnValidate?.Invoke(context) ?? Task.CompletedTask;
    }

    public Task<string> PrepareAsync(PublishContext context, CancellationToken cancellationToken)
    {
        script.Calls.Enqueue(("prepare", context));
        return Task.FromResult(script.Prepare(context));
    }

    public Task<MediaContainerStatus> GetStatusAsync(PublishContext context, CancellationToken cancellationToken)
    {
        script.Calls.Enqueue(("status", context));
        return Task.FromResult(script.Status(context));
    }

    public Task<PublishReceipt> PublishAsync(PublishContext context, CancellationToken cancellationToken)
    {
        script.Calls.Enqueue(("publish", context));
        return script.Publish(context);
    }

    public PublishFailure NormalizeError(Exception error, bool mayHaveCreatedPost) =>
        error as PublishFailure ?? new PublishFailure(mayHaveCreatedPost ? PublishErrorCategory.UNCERTAIN : PublishErrorCategory.RETRYABLE, "FAKE", error.Message);
}
