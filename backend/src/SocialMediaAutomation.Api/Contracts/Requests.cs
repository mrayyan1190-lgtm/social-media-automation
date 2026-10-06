using System.ComponentModel.DataAnnotations;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Api.Contracts;

public sealed record LoginRequest([Required, MaxLength(256)] string Email, [Required, MaxLength(256)] string Password);

public sealed record RegisterRequest(
    [Required, EmailAddress, MaxLength(256)] string Email,
    [Required, MaxLength(256)] string Password,
    [Required, MaxLength(Organization.NameMaxLength)] string WorkspaceName);

/// <summary>Onboarding profile step. OrganizationId defaults to the user's first workspace.</summary>
public sealed record CompleteProfileRequest(
    Guid? OrganizationId,
    [Required, MaxLength(User.DisplayNameMaxLength)] string FullName,
    [Required, MaxLength(64)] string TimeZoneId);

/// <summary>Workspace setup. DefaultLanguage defaults to "en"; the frontend may auto-detect the IANA time zone.</summary>
public sealed record CreateOrganizationRequest(
    [Required, MaxLength(Organization.NameMaxLength)] string Name,
    [Required, MaxLength(Organization.IndustryMaxLength)] string Industry,
    [Required, MaxLength(64)] string TimeZoneId,
    [MaxLength(35)] string? DefaultLanguage = null,
    bool RequireDistinctReviewer = true);

public sealed record UpdateOrganizationSettingsRequest(
    [Required, MaxLength(Organization.NameMaxLength)] string Name,
    [MaxLength(Organization.IndustryMaxLength)] string? Industry,
    [MaxLength(35)] string? DefaultLanguage,
    [Required, MaxLength(64)] string TimeZoneId,
    [Required] bool? RequireDistinctReviewer);

public sealed record ChangeMemberRoleRequest([Required] OrganizationRole? Role);

/// <summary>Adds an existing account (by email) to the workspace with a role.</summary>
public sealed record AddMemberRequest([Required, EmailAddress, MaxLength(256)] string Email, [Required] OrganizationRole? Role);

public sealed record CreateCreatorRequest(
    [Required, MaxLength(200)] string FullName,
    [Required, EmailAddress, MaxLength(256)] string Email,
    [Required, MaxLength(256)] string Password);
