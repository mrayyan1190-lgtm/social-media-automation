using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SocialMediaAutomation.Application.Campaigns.DTOs;
using SocialMediaAutomation.Application.Organizations.DTOs;
using SocialMediaAutomation.Infrastructure.Persistence.Seed;

namespace SocialMediaAutomation.IntegrationTests.Infrastructure;

internal static class ApiClient
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    /// <summary>Signs in through the real login endpoint; the returned client carries the session cookie.</summary>
    public static async Task<HttpClient> SignInAsync(this ApiFactory factory, DemoData.DemoUser user)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { user.Email, Password = factory.DemoPassword });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }

    /// <summary>A valid campaign (folder) request body.</summary>
    public static object CampaignBody(string? name = null, string? color = "#2563EB", string? description = null) => new
    {
        Name = name ?? $"Campaign {Guid.NewGuid():N}",
        Color = color,
        Description = description,
    };

    /// <summary>Creates a campaign folder in the workspace.</summary>
    /// <param name="organizationUrl">e.g. /api/v1/organizations/{id}</param>
    public static Task<CampaignDto> CreateCampaignAsync(this HttpClient client, string organizationUrl) =>
        client.CreateCampaignAsync(organizationUrl, CampaignBody());

    public static async Task<CampaignDto> CreateCampaignAsync(this HttpClient client, string organizationUrl, object body) =>
        await (await client.PostAsJsonAsync($"{organizationUrl}/campaigns", body)).ReadAsync<CampaignDto>();

    /// <summary>Creates a post filed in the campaign at <paramref name="campaignUrl"/> ({org}/campaigns/{id}).</summary>
    public static Task<HttpResponseMessage> CreatePostAsync(this HttpClient client, string campaignUrl, object body)
    {
        var json = JsonSerializer.SerializeToNode(body, Json)!.AsObject();
        json["campaignId"] = campaignUrl.Split('/')[^1];
        return client.PostAsJsonAsync($"{OrganizationOf(campaignUrl)}/posts", json, Json);
    }

    /// <summary>The post list of one campaign.</summary>
    public static string PostsOf(string campaignUrl) => $"{OrganizationOf(campaignUrl)}/posts?campaignId={campaignUrl.Split('/')[^1]}";

    private static string OrganizationOf(string campaignUrl) => campaignUrl[..campaignUrl.IndexOf("/campaigns/", StringComparison.Ordinal)];

    public static object WorkspaceBody(string? name = null, string timeZoneId = "Europe/Paris") =>
        new { Name = name ?? $"Workspace {Guid.NewGuid():N}", Industry = "Retail", TimeZoneId = timeZoneId };

    public static object BrandBody(string description = "We sell outdoor gear.", string tone = "Friendly", string? website = "https://acme.example") =>
        new { BusinessDescription = description, TargetAudience = "Weekend hikers", BrandTone = tone, Website = website, ProhibitedTerms = new[] { "guaranteed" } };

    /// <summary>A fresh workspace owned by the caller (Admin), optionally with a brand profile, isolated from seeded data.</summary>
    public static async Task<OrganizationDto> CreateWorkspaceAsync(this HttpClient client, bool withBrand = true)
    {
        var organization = await (await client.PostAsJsonAsync("/api/v1/organizations", WorkspaceBody())).ReadAsync<OrganizationDto>();
        if (withBrand)
            await (await client.PutJsonAsync($"/api/v1/organizations/{organization.Id}/brand-profile", BrandBody(), ifMatch: null)).ReadAsync<object>();
        return organization;
    }

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"Expected success but got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    /// <summary>Asserts the HTTP status and the stable machine-readable error code.</summary>
    public static async Task AssertErrorAsync(this HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(status == response.StatusCode, $"Expected {(int)status} but got {(int)response.StatusCode}: {body}");
        Assert.Equal(code, JsonDocument.Parse(body).RootElement.GetProperty("code").GetString());
    }

    public static Task<HttpResponseMessage> PutJsonAsync(this HttpClient client, string url, object body, string? ifMatch)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = JsonContent.Create(body, options: Json) };
        if (ifMatch is not null)
            request.Headers.IfMatch.Add(EntityTagHeaderValue.Parse(ifMatch));
        return client.SendAsync(request);
    }

    public static string ETag(byte[] rowVersion) => $"\"{Convert.ToBase64String(rowVersion)}\"";
}
