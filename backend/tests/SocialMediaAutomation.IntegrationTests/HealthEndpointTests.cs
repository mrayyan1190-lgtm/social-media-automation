using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SocialMediaAutomation.IntegrationTests;

public class HealthEndpointTests
{
    [Fact]
    public async Task Health_reports_unhealthy_when_database_is_unreachable()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseEnvironment("Testing")
            .UseSetting("ConnectionStrings:Core",
                "Server=tcp:127.0.0.1,1;Database=Unreachable;User Id=x;Password=x;Connect Timeout=2;TrustServerCertificate=True"));

        var response = await factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync());
    }
}
