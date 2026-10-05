using SocialMediaAutomation.Domain.Common;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Domain.Entities;

/// <summary>
/// A business publishing destination of a workspace (PRD §23). It is not a credential: OAuth grants live separately
/// (TASKS.md Phase 9). Its mode is always visible; simulated destinations never produce real publications.
/// </summary>
public sealed class SocialAccount
{
    public const int DisplayNameMax = 200;

    private SocialAccount() { }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public SocialPlatform Platform { get; private set; }
    public string DisplayName { get; private set; } = null!;

    /// <summary>The provider's page/profile/account id. Null for simulated and not-configured destinations.</summary>
    public string? ExternalAccountId { get; private set; }

    /// <summary>
    /// Set once at creation and never changed by the system: a LIVE destination never silently becomes SIMULATED.
    /// Simulated destinations are separate, explicitly created accounts.
    /// </summary>
    public ExecutionMode ExecutionMode { get; private set; }

    public ConnectionStatus ConnectionStatus { get; private set; }

    /// <summary>The provider grant used to act for this destination (LIVE/TEST_ACCOUNT only).</summary>
    public Guid? OAuthConnectionId { get; private set; }

    /// <summary>What the grant allows here, e.g. "publish_text", "publish_image" (informational, re-checked at dispatch).</summary>
    public List<string> Capabilities { get; private set; } = [];

    public DateTime? LastVerifiedAtUtc { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    /// <summary>Whether content for this destination may enter review and, later, publication.</summary>
    public bool IsEligibleDestination =>
        ExecutionMode is ExecutionMode.LIVE or ExecutionMode.TEST_ACCOUNT or ExecutionMode.SIMULATED
        && ConnectionStatus == ConnectionStatus.CONNECTED;

    /// <summary>
    /// Creates a destination without a provider connection: SIMULATED (transparent demo fallback) or NOT_CONFIGURED
    /// (placeholder until OAuth is connected). LIVE/TEST_ACCOUNT destinations come only from a provider connection.
    /// </summary>
    public static SocialAccount CreateUnconnected(Guid id, Guid organizationId, SocialPlatform platform, string displayName, ExecutionMode mode, Guid userId, DateTime utcNow)
    {
        if (mode is not (ExecutionMode.SIMULATED or ExecutionMode.NOT_CONFIGURED))
            throw Guard.Invalid("Execution mode", "can only be SIMULATED or NOT_CONFIGURED without a provider connection");
        return new SocialAccount
        {
            Id = id,
            OrganizationId = organizationId,
            Platform = Guard.Defined(platform, "Platform"),
            DisplayName = Guard.Required(displayName, "Display name", DisplayNameMax),
            ExecutionMode = mode,
            ConnectionStatus = mode == ExecutionMode.SIMULATED ? ConnectionStatus.CONNECTED : ConnectionStatus.DISCONNECTED,
            CreatedByUserId = userId,
            CreatedAtUtc = utcNow,
            UpdatedAtUtc = utcNow,
        };
    }

    /// <summary>A destination discovered through a provider connection, in the mode the Admin chose when connecting.</summary>
    public static SocialAccount CreateConnected(
        Guid id, OAuthConnection connection, string externalAccountId, string displayName, ExecutionMode mode, IEnumerable<string> capabilities,
        Guid userId, DateTime utcNow)
    {
        if (mode is not (ExecutionMode.LIVE or ExecutionMode.TEST_ACCOUNT))
            throw Guard.Invalid("Execution mode", "must be LIVE or TEST_ACCOUNT for a connected destination");
        var account = new SocialAccount
        {
            Id = id,
            OrganizationId = connection.OrganizationId,
            Platform = connection.Platform,
            ExternalAccountId = Guard.Required(externalAccountId, "Provider account id", 200),
            ExecutionMode = mode,
            CreatedByUserId = userId,
            CreatedAtUtc = utcNow,
        };
        account.RefreshFromConnection(connection, displayName, capabilities, utcNow);
        return account;
    }

    /// <summary>Re-binds an existing connected destination to a fresh grant (reconnect). The mode never changes here.</summary>
    public void RefreshFromConnection(OAuthConnection connection, string displayName, IEnumerable<string> capabilities, DateTime utcNow)
    {
        if (connection.OrganizationId != OrganizationId || connection.Platform != Platform || ExternalAccountId is null)
            throw new InvalidOperationException("A destination can only be refreshed from a grant of its own workspace and platform.");
        DisplayName = Guard.Required(displayName, "Display name", DisplayNameMax);
        OAuthConnectionId = connection.Id;
        Capabilities = capabilities.Distinct().ToList();
        ConnectionStatus = ConnectionStatus.CONNECTED;
        LastVerifiedAtUtc = utcNow;
        UpdatedAtUtc = utcNow;
    }

    /// <summary>The grant was removed: the destination stays visible (with its mode) but can no longer receive content.</summary>
    public void MarkDisconnected(DateTime utcNow)
    {
        ConnectionStatus = ConnectionStatus.DISCONNECTED;
        UpdatedAtUtc = utcNow;
    }
}
