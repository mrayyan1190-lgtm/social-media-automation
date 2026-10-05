using Microsoft.AspNetCore.Mvc;
using SocialMediaAutomation.Api.Contracts;
using SocialMediaAutomation.Application.Authentication.DTOs;
using SocialMediaAutomation.Application.Authentication.Services;

namespace SocialMediaAutomation.Api.Controllers;

[ApiController]
public sealed class MeController(AccountService accounts) : ControllerBase
{
    [HttpGet("api/v1/me")]
    public Task<MeDto> Get(CancellationToken cancellationToken) => accounts.GetMeAsync(cancellationToken);

    /// <summary>Onboarding profile step: full name and workspace time zone. Returns the refreshed session context.</summary>
    [HttpPut("api/v1/me/profile")]
    public Task<MeDto> CompleteProfile(CompleteProfileRequest request, CancellationToken cancellationToken) =>
        accounts.CompleteProfileAsync(new CompleteProfileCommand(request.OrganizationId, request.FullName, request.TimeZoneId), cancellationToken);

    /// <summary>Onboarding progress for the given workspace, or the user's first workspace when omitted.</summary>
    [HttpGet("api/v1/onboarding/status")]
    public Task<OnboardingStatusDto> Onboarding([FromQuery] Guid? organizationId, CancellationToken cancellationToken) =>
        accounts.GetOnboardingAsync(organizationId, cancellationToken);
}
