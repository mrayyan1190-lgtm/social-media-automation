using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.AiGeneration.DTOs;
using SocialMediaAutomation.Application.Common;
using SocialMediaAutomation.Application.Common.Authorization;
using SocialMediaAutomation.Domain.Common;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Application.AiGeneration.Services;

/// <summary>
/// Legacy per-workspace LLM connection, kept as an internal fallback to the system configuration. Internal system
/// administrators only; customers (including workspace Admins) never configure keys or models. Keys go in encrypted and
/// never come back out through the API.
/// </summary>
public sealed class AiSettingsService(
    OrganizationAuthorizer authorizer,
    SystemAdminAuthorizer systemAdmins,
    IAiRepository ai,
    ISecretProtector secrets,
    ILlmClient llm,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<AiSettingsDto> GetAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        await systemAdmins.RequireAsync(cancellationToken);
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ViewOrganization, cancellationToken);
        var settings = await ai.GetSettingsAsync(access.OrganizationId, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.NotFound, "No AI provider is configured for this workspace.");
        return AiSettingsDto.From(settings);
    }

    /// <summary>Creates or updates the connection. A blank key keeps the stored one (only when provider and endpoint are unchanged).</summary>
    public async Task<AiSettingsDto> SaveAsync(
        Guid organizationId, AiProvider provider, string? baseUrl, string model, string? apiKey, byte[]? expectedRowVersion, CancellationToken cancellationToken)
    {
        await systemAdmins.RequireAsync(cancellationToken);
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ManageOrganizationSettings, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var key = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        var settings = await ai.GetSettingsAsync(access.OrganizationId, cancellationToken);

        if (settings is null)
        {
            if (expectedRowVersion is not null)
                throw new BusinessException(ErrorCodes.ConcurrencyConflict, "The AI settings no longer exist. Reload and try again.");
            if (key is null)
                throw Guard.Invalid("API key", "is required");
            settings = AiProviderSettings.Create(Guid.NewGuid(), access.OrganizationId, provider, baseUrl, model, secrets.Protect(key), Hint(key), access.UserId, now);
            ai.Add(settings);
        }
        else
        {
            ConcurrencyGuard.EnsureCurrent(settings.RowVersion, expectedRowVersion);
            // A stored key is only ever sent to the endpoint it was entered for.
            if (key is null && AiProviderSettings.ResolveBaseUrl(provider, baseUrl) != settings.BaseUrl)
                throw Guard.Invalid("API key", "must be re-entered when the provider or endpoint changes");
            settings.Update(provider, baseUrl, model, key is null ? null : secrets.Protect(key), key is null ? null : Hint(key), access.UserId, now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return AiSettingsDto.From(settings);
    }

    /// <summary>Lists the models the provider offers for the given key (or the stored key for the same endpoint).</summary>
    public async Task<IReadOnlyList<string>> ListModelsAsync(
        Guid organizationId, AiProvider provider, string? baseUrl, string? apiKey, CancellationToken cancellationToken)
    {
        await systemAdmins.RequireAsync(cancellationToken);
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ManageOrganizationSettings, cancellationToken);
        var resolvedUrl = AiProviderSettings.ResolveBaseUrl(provider, baseUrl);
        var key = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        if (key is null)
        {
            var stored = await ai.GetSettingsAsync(access.OrganizationId, cancellationToken);
            key = stored is not null && stored.BaseUrl == resolvedUrl
                ? secrets.Unprotect(stored.ProtectedApiKey)
                : throw Guard.Invalid("API key", "is required to list models for this provider");
        }

        try
        {
            return (await llm.ListModelsAsync(new LlmConnection(provider, resolvedUrl, "", key), cancellationToken)).Order(StringComparer.Ordinal).ToList();
        }
        catch (LlmException ex)
        {
            throw new BusinessException(ErrorCodes.ValidationFailed, ex.Kind switch
            {
                LlmErrorKind.Authentication => "The provider rejected the API key.",
                LlmErrorKind.Rejected => $"The provider rejected the request: {ex.Message}",
                _ => $"The provider could not be reached: {ex.Message}",
            });
        }
    }

    private static string Hint(string key) => key.Length <= 4 ? "" : key[^4..];
}
