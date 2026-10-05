using Microsoft.AspNetCore.Mvc;
using SocialMediaAutomation.Application.Dashboard;

namespace SocialMediaAutomation.Api.Controllers;

[ApiController]
[Route("api/v1/organizations/{orgId:guid}/dashboard")]
public sealed class DashboardController(DashboardService dashboard) : ControllerBase
{
    /// <summary>Any member. Workflow counts, pending reviews, upcoming jobs, jobs needing attention and destination status.</summary>
    [HttpGet]
    public Task<DashboardDto> Get(Guid orgId, CancellationToken cancellationToken) => dashboard.GetAsync(orgId, cancellationToken);
}
