using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.Audit.Services;
using SocialMediaAutomation.Application.Common.Authorization;
using SocialMediaAutomation.Application.SocialAccounts.DTOs;
using SocialMediaAutomation.Domain.Common;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Application.SocialAccounts.Services;

/// <summary>
/// Provider connections (ARCHITECTURE.md §23, AGENTS.md §55). The flow starts on the server with a short-lived,
/// one-time state bound to user, workspace, provider and intended mode; the code is exchanged server-side; tokens are
/// stored only in protected form and never returned. Destinations discovered through a grant keep the mode the Admin
/// chose; nothing ever switches a LIVE destination to SIMULATED.
/// </summary>
public sealed class SocialConnectionService(
    OrganizationAuthorizer authorizer,
    IOrganizationRepository organizations,
    IOAuthRepository oauth,
    ISocialAccountRepository accounts,
    ISocialOAuthGateway gateway,
    ISecretProtector secrets,
    ActivityRecorder activity,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public const string ConnectionResource = "OAuthConnection";

    public async Task<IReadOnlyList<SocialProviderDto>> ListProvidersAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        await authorizer.AuthorizeAsync(organizationId, Permission.ViewSocialAccounts, cancellationToken);
        return Enum.GetValues<SocialPlatform>().Select(p => gateway.IsConfigured(p)
            ? new SocialProviderDto(p, true, null)
            : new SocialProviderDto(p, false, $"Live {p} connection is blocked: no {p} app credentials are configured on the server. Use a SIMULATED destination meanwhile."))
            .ToList();
    }

    public async Task<IReadOnlyList<SocialConnectionDto>> ListConnectionsAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ViewSocialAccounts, cancellationToken);
        return await oauth.ListConnectionsAsync(access.OrganizationId, cancellationToken);
    }

    /// <summary>Admin. Issues a one-time state and returns the provider authorization URL the browser should open.</summary>
    public async Task<string> StartAsync(Guid organizationId, SocialPlatform platform, ExecutionMode mode, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ManageSocialAccounts, cancellationToken);
        if (!gateway.IsConfigured(Guard.Defined(platform, "Platform")))
            throw new BusinessException(ErrorCodes.ProviderNotConfigured,
                $"Live {platform} connection is unavailable: no {platform} app credentials are configured on the server.");

        var (state, value) = OAuthState.Issue(access.OrganizationId, access.UserId, platform, mode, Now);
        oauth.Add(state);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return gateway.BuildAuthorizationUrl(platform, value);
    }

    /// <summary>
    /// Handles the provider redirect. It runs without the session cookie (SameSite=Strict cookies are not sent on a
    /// cross-site redirect), so the state alone identifies user and workspace, and membership is re-checked here.
    /// </summary>
    public async Task<OAuthCallbackResult> CompleteAsync(
        SocialPlatform platform, string? stateValue, string? code, string? providerError, CancellationToken cancellationToken)
    {
        var state = string.IsNullOrEmpty(stateValue) ? null : await oauth.FindStateAsync(OAuthState.Hash(stateValue), cancellationToken);
        if (state is null)
            return new(false, "The connection link is invalid. Start the connection again from Settings.");
        try
        {
            state.Consume(platform, Now);
            // Burn the state before any provider call, so a replayed callback can never be processed twice
            // (a concurrent replay loses on the ConsumedAtUtc concurrency token).
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (BusinessException ex)
        {
            return new(false, ex.Code == ErrorCodes.ConcurrencyConflict ? "This connection link was already used. Start the connection again." : ex.Message);
        }

        if (!string.IsNullOrEmpty(providerError))
            return new(false, $"{platform} did not grant access ({Trim(providerError)}).");
        if (string.IsNullOrEmpty(code))
            return new(false, $"{platform} returned no authorization code.");

        var membership = await organizations.GetMembershipAsync(state.OrganizationId, state.UserId, cancellationToken);
        if (membership is null || !membership.Has(Permission.ManageSocialAccounts))
            return new(false, "You are no longer allowed to manage destinations in this workspace.");

        OAuthGrant grant;
        IReadOnlyList<DiscoveredAccount> discovered;
        try
        {
            grant = await gateway.ExchangeCodeAsync(platform, code, cancellationToken);
            discovered = await gateway.DiscoverAccountsAsync(platform, grant, cancellationToken);
        }
        catch (OAuthProviderException ex)
        {
            return new(false, $"{platform} connection failed: {Trim(ex.Message)}");
        }

        var connection = await oauth.FindConnectionAsync(state.OrganizationId, platform, grant.ExternalUserId, cancellationToken);
        if (connection is null)
        {
            connection = OAuthConnection.Create(Guid.NewGuid(), state.OrganizationId, platform, grant.ExternalUserId, grant.DisplayName, state.UserId, Now);
            oauth.Add(connection);
        }
        connection.Grant(secrets.Protect(grant.AccessToken), grant.RefreshToken is null ? null : secrets.Protect(grant.RefreshToken),
            grant.ExpiresAtUtc, grant.Scopes, grant.DisplayName, state.UserId, Now);

        var created = 0;
        var keptMode = 0;
        foreach (var found in discovered)
        {
            var existing = await accounts.FindByExternalIdAsync(state.OrganizationId, platform, found.ExternalAccountId, cancellationToken);
            if (existing is null)
            {
                accounts.Add(SocialAccount.CreateConnected(Guid.NewGuid(), connection, found.ExternalAccountId, found.DisplayName, state.RequestedMode,
                    found.Capabilities, state.UserId, Now));
                created++;
            }
            else
            {
                existing.RefreshFromConnection(connection, found.DisplayName, found.Capabilities, Now);
                if (existing.ExecutionMode != state.RequestedMode)
                    keptMode++;
            }
        }

        activity.Audit(state.OrganizationId, state.UserId, AuditActions.SocialConnectionConnected, ConnectionResource, connection.Id, new Dictionary<string, string?>
        {
            ["platform"] = platform.ToString(), ["requestedMode"] = state.RequestedMode.ToString(),
            ["destinationsFound"] = discovered.Count.ToString(), ["destinationsCreated"] = created.ToString(),
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var message = discovered.Count == 0
            ? $"{platform} connected as {grant.DisplayName}, but no publishable {platform} destinations were found for this login."
            : $"{platform} connected as {grant.DisplayName}: {discovered.Count} destination(s) available ({created} new).";
        if (keptMode > 0)
            message += $" {keptMode} existing destination(s) kept their previous execution mode; modes are never changed automatically.";
        return new(true, message);
    }

    /// <summary>Admin. Destroys the stored tokens and makes every destination using this grant ineligible.</summary>
    public async Task DisconnectAsync(Guid organizationId, Guid connectionId, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ManageSocialAccounts, cancellationToken);
        var connection = await oauth.GetConnectionAsync(connectionId, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.NotFound, "Connection not found.");
        access.EnsureOwns(connection.OrganizationId, "Connection");

        connection.Revoke(Now);
        var affected = await accounts.ListByConnectionAsync(access.OrganizationId, connection.Id, cancellationToken);
        foreach (var account in affected)
            account.MarkDisconnected(Now);

        activity.Audit(access.OrganizationId, access.UserId, AuditActions.SocialConnectionDisconnected, ConnectionResource, connection.Id,
            new Dictionary<string, string?> { ["platform"] = connection.Platform.ToString(), ["destinationsDisconnected"] = affected.Count.ToString() });
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    private static string Trim(string text) => text.Length > 200 ? text[..200] : text;
}
