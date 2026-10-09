using System.Text;
using SocialMediaAutomation.Application.SocialAccounts.DTOs;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Application.Abstractions;

/// <summary>A provider grant right after code exchange. ToString is redacted so tokens cannot reach a log by accident.</summary>
public sealed record OAuthGrant(
    string AccessToken,
    string? RefreshToken,
    DateTime? ExpiresAtUtc,
    IReadOnlyList<string> Scopes,
    string ExternalUserId,
    string DisplayName)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"ExternalUserId = {ExternalUserId}, DisplayName = {DisplayName}, Scopes = [{string.Join(' ', Scopes)}], AccessToken = ***");
        return true;
    }
}

/// <summary>A publishable destination the grant can act for (a LinkedIn member, a Facebook Page, an Instagram professional account).</summary>
public sealed record DiscoveredAccount(string ExternalAccountId, string DisplayName, IReadOnlyList<string> Capabilities);

/// <summary>The provider refused or failed an OAuth step. The message is safe to show; it never contains secrets.</summary>
public sealed class OAuthProviderException(string message) : Exception(message);

/// <summary>Provider-specific OAuth: authorization URL, server-side code exchange and destination discovery.</summary>
public interface ISocialOAuthGateway
{
    /// <summary>Whether app credentials for this provider are configured. If not, live connection is honestly unavailable.</summary>
    bool IsConfigured(SocialPlatform platform);

    string BuildAuthorizationUrl(SocialPlatform platform, string state);

    Task<OAuthGrant> ExchangeCodeAsync(SocialPlatform platform, string code, CancellationToken cancellationToken);

    Task<IReadOnlyList<DiscoveredAccount>> DiscoverAccountsAsync(SocialPlatform platform, OAuthGrant grant, CancellationToken cancellationToken);

    /// <summary>
    /// Checks a pasted Facebook Page access token with Meta: valid, a PAGE token, issued to our Meta app. Returns it as a
    /// grant whose ExternalUserId/DisplayName are the Page's id and name.
    /// </summary>
    Task<OAuthGrant> InspectFacebookPageTokenAsync(string pageToken, CancellationToken cancellationToken);
}

public interface IOAuthRepository
{
    void Add(OAuthState state);

    /// <summary>Tracked; by the hash of the state value.</summary>
    Task<OAuthState?> FindStateAsync(string stateHash, CancellationToken cancellationToken);

    void Add(OAuthConnection connection);

    /// <summary>Tracked; loads by id only, so callers verify OrganizationId.</summary>
    Task<OAuthConnection?> GetConnectionAsync(Guid connectionId, CancellationToken cancellationToken);

    /// <summary>Tracked; the workspace's existing grant for this provider user, if any.</summary>
    Task<OAuthConnection?> FindConnectionAsync(Guid organizationId, SocialPlatform platform, string externalUserId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SocialConnectionDto>> ListConnectionsAsync(Guid organizationId, CancellationToken cancellationToken);
}
