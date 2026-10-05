using SocialMediaAutomation.Domain.Entities;

namespace SocialMediaAutomation.Application.Abstractions;

/// <summary>
/// Platform secrets (LLM API key, provider client secrets). Values go in and are read back only by backend code that
/// calls the provider; they are never returned by an API. The MVP implementation encrypts them in SQL with the shared
/// data-protection key ring; a managed store (e.g. Azure Key Vault) implements the same contract in production.
/// </summary>
public interface ISecretStore
{
    /// <summary>Stages the value in the current unit of work (committed together with the configuration change).</summary>
    Task SetAsync(string name, string value, CancellationToken cancellationToken);

    Task<string?> GetAsync(string name, CancellationToken cancellationToken);
}

/// <summary>The correlation id of the current operation (API request), or null outside a request.</summary>
public interface ICorrelationContext
{
    string? CorrelationId { get; }
}

/// <summary>Operational events for the System log. Writes on its own and never throws: logging must not break the caller.</summary>
public interface ISystemEventLog
{
    Task RecordAsync(string level, string service, string eventName, string message, string? correlationId = null, CancellationToken cancellationToken = default);
}

/// <summary>Platform-owned configuration and logs.</summary>
public interface IPlatformRepository
{
    Task<PlatformAiConfiguration?> GetAiConfigurationAsync(CancellationToken cancellationToken);

    Task<SocialProviderConfiguration?> GetProviderAsync(PlatformProvider provider, CancellationToken cancellationToken);

    Task<IReadOnlyList<SocialProviderConfiguration>> ListProvidersAsync(CancellationToken cancellationToken);

    void Add(PlatformAiConfiguration configuration);

    void Add(SocialProviderConfiguration configuration);

    void Add(PlatformAuditLog entry);

    void Add(SignInEvent signIn);
}

/// <summary>Reloads provider app credentials after an operator changes them (the OAuth gateway reads a cached snapshot).</summary>
public interface IProviderAppCache
{
    Task RefreshAsync(CancellationToken cancellationToken);
}

/// <summary>Checks provider app credentials against the provider, where the provider offers a way to do so.</summary>
public interface IProviderAppValidator
{
    /// <returns>Null when the provider has no credential check (configuration completeness only).</returns>
    Task<(bool Valid, string Message)?> ValidateAsync(PlatformProvider provider, string clientId, string clientSecret, CancellationToken cancellationToken);
}

/// <summary>An actual storage round trip, for System Health. Throws when storage is not usable.</summary>
public interface IStorageProbe
{
    Task ProbeAsync(CancellationToken cancellationToken);
}
