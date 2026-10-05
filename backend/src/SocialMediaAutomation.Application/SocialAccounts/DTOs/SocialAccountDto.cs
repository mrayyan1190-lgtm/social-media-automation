using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Application.SocialAccounts.DTOs;

/// <summary>Destination metadata only; credentials never appear in any DTO.</summary>
public sealed record SocialAccountDto(
    Guid Id,
    SocialPlatform Platform,
    string DisplayName,
    ExecutionMode ExecutionMode,
    ConnectionStatus ConnectionStatus,
    bool IsEligibleDestination,
    DateTime CreatedAtUtc,
    string? ExternalAccountId = null,
    IReadOnlyList<string>? Capabilities = null,
    DateTime? LastVerifiedAtUtc = null)
{
    public static SocialAccountDto From(SocialAccount a) =>
        new(a.Id, a.Platform, a.DisplayName, a.ExecutionMode, a.ConnectionStatus, a.IsEligibleDestination, a.CreatedAtUtc,
            a.ExternalAccountId, a.Capabilities, a.LastVerifiedAtUtc);
}

/// <summary>A provider grant as shown to users: who and what was connected, never the tokens.</summary>
public sealed record SocialConnectionDto(
    Guid Id,
    SocialPlatform Platform,
    string DisplayName,
    OAuthConnectionStatus Status,
    IReadOnlyList<string> Scopes,
    DateTime ConnectedAtUtc,
    DateTime? AccessTokenExpiresAtUtc,
    int DestinationCount);

/// <summary>Whether live connection is possible for a provider in this deployment.</summary>
public sealed record SocialProviderDto(SocialPlatform Platform, bool LiveConnectionAvailable, string? BlockedReason);

/// <summary>Outcome of an OAuth callback, used to redirect the browser back to the app with a safe message.</summary>
public sealed record OAuthCallbackResult(bool Succeeded, string Message);
