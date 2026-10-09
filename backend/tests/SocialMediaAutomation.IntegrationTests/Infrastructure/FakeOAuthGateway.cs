using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.IntegrationTests.Infrastructure;

/// <summary>
/// Stand-in for the provider OAuth endpoints. LinkedIn and Facebook count as configured, Instagram does not.
/// The authorization code names the provider user; "fail" makes the exchange fail and "none" discovers nothing.
/// </summary>
public sealed class FakeOAuthGateway : ISocialOAuthGateway
{
    public static string AccessToken(string code) => $"raw-access-token-{code}";

    public static string RefreshToken(string code) => $"raw-refresh-token-{code}";

    public bool IsConfigured(SocialPlatform platform) => platform != SocialPlatform.Instagram;

    public string BuildAuthorizationUrl(SocialPlatform platform, string state) =>
        $"https://provider.test/{platform}/authorize?state={Uri.EscapeDataString(state)}";

    public Task<OAuthGrant> ExchangeCodeAsync(SocialPlatform platform, string code, CancellationToken cancellationToken) =>
        code == "fail"
            ? throw new OAuthProviderException("token exchange failed: invalid_grant")
            : Task.FromResult(new OAuthGrant(AccessToken(code), RefreshToken(code), DateTime.UtcNow.AddHours(1), ["w_member_social"], code, $"User {code}"));

    public Task<IReadOnlyList<DiscoveredAccount>> DiscoverAccountsAsync(SocialPlatform platform, OAuthGrant grant, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DiscoveredAccount>>(grant.ExternalUserId.StartsWith("none", StringComparison.Ordinal)
            ? []
            : [new DiscoveredAccount($"acct-{grant.ExternalUserId}", $"{platform} page {grant.ExternalUserId}", ["publish_text"])]);

    public Task<OAuthGrant> InspectFacebookPageTokenAsync(string pageToken, CancellationToken cancellationToken) =>
        pageToken == "fail"
            ? throw new OAuthProviderException("this is not a Page access token.")
            : Task.FromResult(new OAuthGrant(pageToken, null, DateTime.UtcNow.AddDays(60), ["pages_manage_posts"], $"page-{pageToken}", $"Page {pageToken}"));
}
