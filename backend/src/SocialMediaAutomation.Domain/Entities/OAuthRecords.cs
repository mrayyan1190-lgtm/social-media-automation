using System.Security.Cryptography;
using System.Text;
using SocialMediaAutomation.Domain.Common;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Domain.Entities;

public enum OAuthConnectionStatus
{
    ACTIVE,
    EXPIRED,
    REVOKED,
}

/// <summary>
/// A provider grant (credential) for one workspace: who connected it, which provider user it belongs to, and the
/// protected tokens. Never a business destination itself (that is <see cref="SocialAccount"/>); tokens are stored only
/// in protected (encrypted) form and never leave the backend.
/// </summary>
public sealed class OAuthConnection
{
    private OAuthConnection() { }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public SocialPlatform Platform { get; private set; }
    public string ExternalUserId { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public string ProtectedAccessToken { get; private set; } = null!;
    public string? ProtectedRefreshToken { get; private set; }
    public DateTime? AccessTokenExpiresAtUtc { get; private set; }
    public List<string> Scopes { get; private set; } = [];
    public OAuthConnectionStatus Status { get; private set; }
    public Guid ConnectedByUserId { get; private set; }
    public DateTime ConnectedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static OAuthConnection Create(Guid id, Guid organizationId, SocialPlatform platform, string externalUserId, string displayName, Guid userId, DateTime utcNow) => new()
    {
        Id = id,
        OrganizationId = organizationId,
        Platform = Guard.Defined(platform, "Platform"),
        ExternalUserId = Guard.Required(externalUserId, "Provider user id", 200),
        DisplayName = Guard.Required(displayName, "Display name", SocialAccount.DisplayNameMax),
        ConnectedByUserId = userId,
        ConnectedAtUtc = utcNow,
    };

    /// <summary>Stores a fresh grant (initial connection or reconnection). Values must already be protected.</summary>
    public void Grant(string protectedAccessToken, string? protectedRefreshToken, DateTime? expiresAtUtc, IEnumerable<string> scopes, string displayName, Guid userId, DateTime utcNow)
    {
        ProtectedAccessToken = Guard.Required(protectedAccessToken, "Access token", 8000);
        ProtectedRefreshToken = protectedRefreshToken;
        AccessTokenExpiresAtUtc = expiresAtUtc;
        Scopes = scopes.Select(s => s.Trim()).Where(s => s.Length > 0).Distinct().ToList();
        DisplayName = Guard.Required(displayName, "Display name", SocialAccount.DisplayNameMax);
        Status = OAuthConnectionStatus.ACTIVE;
        ConnectedByUserId = userId;
        UpdatedAtUtc = utcNow;
    }

    /// <summary>Destroys the stored credentials. Destinations that depend on this grant become ineligible.</summary>
    public void Revoke(DateTime utcNow)
    {
        ProtectedAccessToken = "";
        ProtectedRefreshToken = null;
        AccessTokenExpiresAtUtc = null;
        Status = OAuthConnectionStatus.REVOKED;
        UpdatedAtUtc = utcNow;
    }
}

/// <summary>
/// Short-lived, one-time CSRF protection for an OAuth round trip, bound to the user, workspace, provider and intended
/// execution mode. Only a SHA-256 hash of the state value is stored, so a database reader cannot forge a callback.
/// </summary>
public sealed class OAuthState
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private OAuthState() { }

    public Guid Id { get; private set; }
    public string StateHash { get; private set; } = null!;
    public Guid OrganizationId { get; private set; }
    public Guid UserId { get; private set; }
    public SocialPlatform Platform { get; private set; }
    public ExecutionMode RequestedMode { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? ConsumedAtUtc { get; private set; }

    /// <summary>Creates the stored half of the state and returns the random value that travels to the provider.</summary>
    public static (OAuthState State, string Value) Issue(Guid organizationId, Guid userId, SocialPlatform platform, ExecutionMode mode, DateTime utcNow)
    {
        if (mode is not (ExecutionMode.LIVE or ExecutionMode.TEST_ACCOUNT))
            throw Guard.Invalid("Execution mode", "must be LIVE or TEST_ACCOUNT for a provider connection");
        var value = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (new OAuthState
        {
            Id = Guid.NewGuid(),
            StateHash = Hash(value),
            OrganizationId = organizationId,
            UserId = userId,
            Platform = Guard.Defined(platform, "Platform"),
            RequestedMode = mode,
            CreatedAtUtc = utcNow,
            ExpiresAtUtc = utcNow.Add(Lifetime),
        }, value);
    }

    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>Validates and consumes the state. Unknown, expired, replayed or cross-provider states are rejected.</summary>
    public void Consume(SocialPlatform platform, DateTime utcNow)
    {
        if (ConsumedAtUtc is not null)
            throw new BusinessException(ErrorCodes.OAuthStateInvalid, "This connection link was already used.");
        if (utcNow > ExpiresAtUtc)
            throw new BusinessException(ErrorCodes.OAuthStateInvalid, "This connection link expired. Start the connection again.");
        if (platform != Platform)
            throw new BusinessException(ErrorCodes.OAuthStateInvalid, "The callback does not match the provider this connection was started for.");
        ConsumedAtUtc = utcNow;
    }
}
