using Microsoft.AspNetCore.Mvc;
using SocialMediaAutomation.Application.Operations;

namespace SocialMediaAutomation.Api.Controllers;

[ApiController]
[Route("api/v1/organizations/{orgId:guid}/operations")]
public sealed class OperationsController(OperationsService operations) : ControllerBase
{
    /// <summary>System admin. Worker liveness, queue age, expired leases, throttling, action-required and reconciliation counts.</summary>
    [HttpGet]
    public Task<OperationsDto> Get(Guid orgId, CancellationToken cancellationToken) => operations.GetAsync(orgId, cancellationToken);
}
