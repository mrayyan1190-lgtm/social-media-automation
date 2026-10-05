using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Domain.Tests;

public class SocialConnectionTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();
    private static readonly DateTime Now = DateTime.UtcNow;

    private static void AssertStateInvalid(Action act) =>
        Assert.Equal(ErrorCodes.OAuthStateInvalid, Assert.Throws<BusinessException>(act).Code);

    [Fact]
    public void State_stores_only_a_hash_of_a_random_value()
    {
        var (state, value) = OAuthState.Issue(Org, Admin, SocialPlatform.LinkedIn, ExecutionMode.LIVE, Now);
        var (_, other) = OAuthState.Issue(Org, Admin, SocialPlatform.LinkedIn, ExecutionMode.LIVE, Now);

        Assert.True(value.Length >= 43);
        Assert.NotEqual(value, other);
        Assert.NotEqual(value, state.StateHash);
        Assert.Equal(OAuthState.Hash(value), state.StateHash);
        Assert.Equal(Now + OAuthState.Lifetime, state.ExpiresAtUtc);
    }

    [Fact]
    public void State_is_one_time_and_bound_to_provider_and_expiry()
    {
        var (state, _) = OAuthState.Issue(Org, Admin, SocialPlatform.Facebook, ExecutionMode.TEST_ACCOUNT, Now);
        AssertStateInvalid(() => state.Consume(SocialPlatform.Instagram, Now));
        state.Consume(SocialPlatform.Facebook, Now);
        AssertStateInvalid(() => state.Consume(SocialPlatform.Facebook, Now));

        var (expired, _) = OAuthState.Issue(Org, Admin, SocialPlatform.Facebook, ExecutionMode.LIVE, Now);
        AssertStateInvalid(() => expired.Consume(SocialPlatform.Facebook, Now + OAuthState.Lifetime + TimeSpan.FromSeconds(1)));
    }

    [Theory]
    [InlineData(ExecutionMode.SIMULATED)]
    [InlineData(ExecutionMode.NOT_CONFIGURED)]
    public void Provider_connection_cannot_be_started_for_simulated_or_unconfigured_mode(ExecutionMode mode) =>
        Assert.Throws<BusinessException>(() => OAuthState.Issue(Org, Admin, SocialPlatform.LinkedIn, mode, Now));

    [Fact]
    public void Connected_destination_keeps_its_mode_across_reconnect_and_becomes_ineligible_on_disconnect()
    {
        var connection = OAuthConnection.Create(Guid.NewGuid(), Org, SocialPlatform.LinkedIn, "member-1", "Ada", Admin, Now);
        var account = SocialAccount.CreateConnected(Guid.NewGuid(), connection, "urn:li:person:member-1", "Ada", ExecutionMode.TEST_ACCOUNT, ["publish_text"], Admin, Now);
        Assert.True(account.IsEligibleDestination);
        Assert.Equal(connection.Id, account.OAuthConnectionId);

        account.MarkDisconnected(Now);
        Assert.False(account.IsEligibleDestination);
        Assert.Equal(ExecutionMode.TEST_ACCOUNT, account.ExecutionMode); // never silently becomes SIMULATED

        account.RefreshFromConnection(connection, "Ada L.", ["publish_text", "publish_image"], Now);
        Assert.True(account.IsEligibleDestination);
        Assert.Equal((ExecutionMode.TEST_ACCOUNT, "Ada L.", 2), (account.ExecutionMode, account.DisplayName, account.Capabilities.Count));
    }

    [Fact]
    public void Connected_destination_requires_a_live_or_test_mode_and_a_grant_of_its_own_workspace()
    {
        var connection = OAuthConnection.Create(Guid.NewGuid(), Org, SocialPlatform.Facebook, "u1", "Ada", Admin, Now);
        Assert.Throws<BusinessException>(() =>
            SocialAccount.CreateConnected(Guid.NewGuid(), connection, "page-1", "Page", ExecutionMode.SIMULATED, [], Admin, Now));

        var account = SocialAccount.CreateConnected(Guid.NewGuid(), connection, "page-1", "Page", ExecutionMode.LIVE, [], Admin, Now);
        var foreign = OAuthConnection.Create(Guid.NewGuid(), Guid.NewGuid(), SocialPlatform.Facebook, "u1", "Eve", Admin, Now);
        Assert.Throws<InvalidOperationException>(() => account.RefreshFromConnection(foreign, "Page", [], Now));
    }

    [Fact]
    public void Not_configured_destination_is_never_eligible_and_simulated_is_explicit()
    {
        var notConfigured = SocialAccount.CreateUnconnected(Guid.NewGuid(), Org, SocialPlatform.Instagram, "IG", ExecutionMode.NOT_CONFIGURED, Admin, Now);
        var simulated = SocialAccount.CreateUnconnected(Guid.NewGuid(), Org, SocialPlatform.Instagram, "IG sim", ExecutionMode.SIMULATED, Admin, Now);
        Assert.False(notConfigured.IsEligibleDestination);
        Assert.True(simulated.IsEligibleDestination);
        Assert.Null(simulated.ExternalAccountId);
        Assert.Throws<BusinessException>(() =>
            SocialAccount.CreateUnconnected(Guid.NewGuid(), Org, SocialPlatform.Instagram, "IG", ExecutionMode.LIVE, Admin, Now));
    }

    [Fact]
    public void Revoking_a_connection_destroys_token_material()
    {
        var connection = OAuthConnection.Create(Guid.NewGuid(), Org, SocialPlatform.LinkedIn, "m", "Ada", Admin, Now);
        connection.Grant("protected-access", "protected-refresh", Now.AddHours(1), ["w_member_social"], "Ada", Admin, Now);
        connection.Revoke(Now);
        Assert.Equal(("", null, OAuthConnectionStatus.REVOKED), (connection.ProtectedAccessToken, connection.ProtectedRefreshToken, connection.Status));
    }
}
