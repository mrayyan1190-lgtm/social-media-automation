using System.Text.Json;
using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.Audit.Services;
using SocialMediaAutomation.Application.Common.Authorization;
using SocialMediaAutomation.Application.SocialAccounts.Services;
using SocialMediaAutomation.Domain.Common;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Application.Platform;

public sealed record PageTokenConnectionDto(Guid SocialAccountId, string PageName, DateTime? ExpiresAtUtc, IReadOnlyList<string> Capabilities);

/// <summary>
/// Approved exception to the OAuth-only connection rule (ARCHITECTURE.md §23.1): a system administrator pastes a Facebook
/// Page access token for a workspace. The server checks it with Meta (valid, issued to our Meta app, a Page token), then
/// stores it protected exactly like an OAuth grant; it is never returned. The destination is LIVE and goes through the
/// same review, approval and publication-job pipeline as any other. Optionally the checked token becomes the default
/// that every workspace created later is connected with.
/// </summary>
public sealed class PageTokenConnectionService(
    SystemAdminAuthorizer systemAdmins,
    IOrganizationRepository organizations,
    IOAuthRepository oauth,
    ISocialAccountRepository accounts,
    ISocialOAuthGateway gateway,
    ISecretProtector secrets,
    ISecretStore secretStore,
    ActivityRecorder activity,
    PlatformAudit audit,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    /// <summary>Marks a destination whose stored token already is the Page token (the publisher skips the Page-token lookup).</summary>
    public const string PageTokenCapability = "page_token";

    /// <summary>The checked Page grant (token included) that new workspaces are connected with; encrypted in the secret store.</summary>
    public const string DefaultPageSecret = "platform/facebook-page-token/default";

    private sealed record DefaultPage(string Token, string PageId, string PageName, List<string> Scopes, DateTime? ExpiresAtUtc);

    public async Task<PageTokenConnectionDto> ConnectFacebookPageAsync(Guid organizationId, string pageToken, bool applyToNewWorkspaces,
        CancellationToken cancellationToken)
    {
        await systemAdmins.RequireAsync(cancellationToken);
        var token = Guard.Required(pageToken, "Page access token", 2000);
        var organization = await organizations.GetAsync(organizationId, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.NotFound, "Workspace not found.");

        OAuthGrant grant;
        try
        {
            grant = await gateway.InspectFacebookPageTokenAsync(token, cancellationToken);
        }
        catch (OAuthProviderException ex)
        {
            throw new BusinessException(ErrorCodes.ValidationFailed, $"Facebook rejected the token: {ex.Message}");
        }

        // Connection and destination rows must name a member of their workspace (composite FK to OrganizationMembers); a system
        // administrator usually is not one, so they are recorded under the workspace's Admin. The audit entries name the actor.
        var userId = currentUser.UserId!.Value;
        var admins = await organizations.ListActiveMemberUserIdsAsync(organization.Id, [OrganizationRole.Admin], cancellationToken);
        var owner = admins.Contains(userId) ? userId : admins.Count > 0 ? admins[0]
            : throw new BusinessException(ErrorCodes.ValidationFailed, $"The workspace \"{organization.Name}\" has no active Admin to own the connection.");
        var account = await AttachAsync(organization.Id, grant, owner, "PAGE_TOKEN", cancellationToken, actorUserId: userId);
        if (applyToNewWorkspaces)
            await secretStore.SetAsync(DefaultPageSecret, JsonSerializer.Serialize(new DefaultPage(grant.AccessToken, grant.ExternalUserId,
                grant.DisplayName, grant.Scopes.ToList(), grant.ExpiresAtUtc)), cancellationToken);
        audit.Record(PlatformAuditActions.FacebookPageTokenConnected, "Workspace", organization.Id.ToString(), organization.Name, organization.Id,
            metadata: new Dictionary<string, string?> { ["pageId"] = grant.ExternalUserId, ["defaultForNewWorkspaces"] = applyToNewWorkspaces.ToString() });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new PageTokenConnectionDto(account.Id, grant.DisplayName, grant.ExpiresAtUtc, account.Capabilities);
    }

    /// <summary>
    /// Called while a new workspace is being created (same unit of work, no Meta call): connects the default Page, if one
    /// was set and its token has not expired. Without a default this does nothing.
    /// </summary>
    public async Task AttachDefaultPageAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken)
    {
        if (await secretStore.GetAsync(DefaultPageSecret, cancellationToken) is not { Length: > 0 } json
            || JsonSerializer.Deserialize<DefaultPage>(json) is not { } page
            || page.ExpiresAtUtc <= Now)
            return;
        await AttachAsync(organizationId, new OAuthGrant(page.Token, null, page.ExpiresAtUtc, page.Scopes, page.PageId, page.PageName),
            userId, "DEFAULT_PAGE_TOKEN", cancellationToken);
    }

    private async Task<SocialAccount> AttachAsync(Guid organizationId, OAuthGrant grant, Guid userId, string method, CancellationToken cancellationToken,
        Guid? actorUserId = null)
    {
        var connection = await oauth.FindConnectionAsync(organizationId, SocialPlatform.Facebook, grant.ExternalUserId, cancellationToken);
        if (connection is null)
        {
            connection = OAuthConnection.Create(Guid.NewGuid(), organizationId, SocialPlatform.Facebook, grant.ExternalUserId, grant.DisplayName, userId, Now);
            oauth.Add(connection);
        }
        connection.Grant(secrets.Protect(grant.AccessToken), null, grant.ExpiresAtUtc, grant.Scopes, grant.DisplayName, userId, Now);

        List<string> capabilities = grant.Scopes.Contains("pages_manage_posts")
            ? ["publish_text", "publish_link", "publish_image", PageTokenCapability]
            : [PageTokenCapability];
        var displayName = $"{grant.DisplayName} (Facebook Page)";
        var account = await accounts.FindByExternalIdAsync(organizationId, SocialPlatform.Facebook, grant.ExternalUserId, cancellationToken);
        if (account is null)
        {
            account = SocialAccount.CreateConnected(Guid.NewGuid(), connection, grant.ExternalUserId, displayName, ExecutionMode.LIVE, capabilities, userId, Now);
            accounts.Add(account);
        }
        else
        {
            account.RefreshFromConnection(connection, displayName, capabilities, Now);
        }

        activity.Audit(organizationId, actorUserId ?? userId, AuditActions.SocialConnectionConnected, SocialConnectionService.ConnectionResource, connection.Id,
            new Dictionary<string, string?> { ["platform"] = "Facebook", ["pageId"] = grant.ExternalUserId, ["method"] = method });
        return account;
    }

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;
}
