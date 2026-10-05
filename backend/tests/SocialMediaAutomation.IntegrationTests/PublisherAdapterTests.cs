using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Domain.Common;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Infrastructure.Publishing;

namespace SocialMediaAutomation.IntegrationTests;

/// <summary>
/// The real provider adapters against a stub HTTP endpoint. This proves request shape and error normalization only;
/// no real LinkedIn/Meta endpoint is called, so live publishing itself remains unverified.
/// </summary>
public class PublisherAdapterTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request, body));
            return respond(request, body);
        }
    }

    private static readonly IConfiguration Config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["SocialProviders:LinkedIn:ApiVersion"] = "202509", ["SocialProviders:Facebook:GraphApiVersion"] = "v21.0",
        ["SocialProviders:Instagram:GraphApiVersion"] = "v21.0",
    }).Build();

    private static readonly ApprovalPayload Payload = new(ApprovalPayload.CurrentVersion, "LinkedIn", Guid.NewGuid(), "Month-end (finally) easy",
        "Close the books in days.", "Book a demo", "https://brightline.example/demo", ["FinanceOps"], null, null, null, null);

    private static PublishContext Context(SocialPlatform platform, string account, string[] capabilities, PublishMedia? media = null, string? container = null) =>
        new(Guid.NewGuid(), platform, ExecutionMode.LIVE, account, capabilities, Payload, media, "secret-user-token", container);

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK, params (string Name, string Value)[] headers)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        foreach (var (name, value) in headers)
            response.Headers.TryAddWithoutValidation(name, value);
        return response;
    }

    private static HttpClient Client(StubHandler handler) => new(handler, disposeHandler: false);

    // ---- LinkedIn --------------------------------------------------------------------------------------

    [Fact]
    public async Task LinkedIn_posts_escaped_commentary_as_the_member_and_returns_the_post_urn()
    {
        var handler = new StubHandler((_, _) => Json("", HttpStatusCode.Created, ("x-restli-id", "urn:li:share:123"), ("x-li-uuid", "abc-req")));
        var publisher = new LinkedInPublisher(Client(handler), Config);
        var context = Context(SocialPlatform.LinkedIn, "urn:li:person:xyz", ["publish_text"]);

        await publisher.ValidateCapabilityAsync(context, CancellationToken.None);
        var receipt = await publisher.PublishAsync(context, CancellationToken.None);

        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal("https://api.linkedin.com/rest/posts", request.RequestUri!.ToString());
        Assert.Equal(("Bearer", "secret-user-token"), (request.Headers.Authorization!.Scheme, request.Headers.Authorization.Parameter));
        Assert.Equal("202509", request.Headers.GetValues("LinkedIn-Version").Single());
        var json = JsonNode.Parse(body!)!;
        Assert.Equal("urn:li:person:xyz", json["author"]!.GetValue<string>());
        var commentary = json["commentary"]!.GetValue<string>();
        Assert.Contains(@"Month-end \(finally\) easy", commentary);
        Assert.Contains(@"{hashtag|\#|FinanceOps}", commentary);
        Assert.Contains("https://brightline.example/demo", commentary);
        Assert.DoesNotContain("secret-user-token", body);
        Assert.Equal(("urn:li:share:123", "https://www.linkedin.com/feed/update/urn:li:share:123/", "abc-req"),
            (receipt.ExternalId, receipt.ExternalUrl, receipt.ProviderRequestId));
    }

    [Fact]
    public async Task LinkedIn_uploads_the_image_before_creating_the_post()
    {
        var handler = new StubHandler((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/rest/images" => Json("""{"value":{"uploadUrl":"https://upload.test/put","image":"urn:li:image:9"}}"""),
            "/put" => Json("", HttpStatusCode.Created),
            _ => Json("", HttpStatusCode.Created, ("x-restli-id", "urn:li:share:9")),
        });
        var media = new PublishMedia(Guid.NewGuid(), "image/jpeg", new string('a', 64), "Dashboard", _ => Task.FromResult<Stream>(new MemoryStream([1, 2, 3])), null);

        await new LinkedInPublisher(Client(handler), Config).PublishAsync(Context(SocialPlatform.LinkedIn, "urn:li:person:xyz", ["publish_image"], media), CancellationToken.None);

        Assert.Equal(["/rest/images", "/put", "/rest/posts"], handler.Requests.Select(r => r.Request.RequestUri!.AbsolutePath));
        var post = JsonNode.Parse(handler.Requests[2].Body!)!;
        Assert.Equal(("urn:li:image:9", "Dashboard"), (post["content"]!["media"]!["id"]!.GetValue<string>(), post["content"]!["media"]!["altText"]!.GetValue<string>()));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, PublishErrorCategory.ACTION_REQUIRED, "AUTH_INVALID")]
    [InlineData(HttpStatusCode.Forbidden, PublishErrorCategory.ACTION_REQUIRED, "PERMISSION_MISSING")]
    [InlineData(HttpStatusCode.TooManyRequests, PublishErrorCategory.RETRYABLE, "THROTTLED")]
    [InlineData(HttpStatusCode.UnprocessableEntity, PublishErrorCategory.ACTION_REQUIRED, "PROVIDER_REJECTED")]
    [InlineData(HttpStatusCode.InternalServerError, PublishErrorCategory.UNCERTAIN, "PROVIDER_ERROR_AFTER_SEND")]
    [InlineData(HttpStatusCode.BadGateway, PublishErrorCategory.UNCERTAIN, "PROVIDER_ERROR_AFTER_SEND")]
    public async Task LinkedIn_errors_on_the_create_call_are_normalized(HttpStatusCode status, PublishErrorCategory category, string code)
    {
        var handler = new StubHandler((_, _) => Json("""{"message":"nope","status":0}""", status));
        var failure = await Assert.ThrowsAsync<PublishFailure>(() =>
            new LinkedInPublisher(Client(handler), Config).PublishAsync(Context(SocialPlatform.LinkedIn, "urn:li:person:x", ["publish_text"]), CancellationToken.None));
        Assert.Equal((category, code), (failure.Category, failure.Code));
        Assert.DoesNotContain("secret-user-token", failure.Message);
    }

    [Fact]
    public async Task Network_failures_are_safe_only_when_the_request_cannot_have_been_sent()
    {
        var refused = new StubHandler((_, _) => throw new HttpRequestException(HttpRequestError.ConnectionError, "refused", new SocketException()));
        var timedOut = new StubHandler((_, _) => throw new TaskCanceledException("timeout"));
        var context = Context(SocialPlatform.LinkedIn, "urn:li:person:x", ["publish_text"]);

        var before = await Assert.ThrowsAsync<PublishFailure>(() => new LinkedInPublisher(Client(refused), Config).PublishAsync(context, CancellationToken.None));
        var after = await Assert.ThrowsAsync<PublishFailure>(() => new LinkedInPublisher(Client(timedOut), Config).PublishAsync(context, CancellationToken.None));

        Assert.Equal((PublishErrorCategory.RETRYABLE, "NETWORK_BEFORE_SEND"), (before.Category, before.Code));
        Assert.Equal((PublishErrorCategory.UNCERTAIN, "TIMEOUT_AFTER_SEND"), (after.Category, after.Code));
    }

    [Fact]
    public async Task LinkedIn_without_the_granted_capability_requires_action()
    {
        var publisher = new LinkedInPublisher(Client(new StubHandler((_, _) => Json("{}"))), Config);
        var failure = await Assert.ThrowsAsync<PublishFailure>(() =>
            publisher.ValidateCapabilityAsync(Context(SocialPlatform.LinkedIn, "urn:li:person:x", []), CancellationToken.None));
        Assert.Equal((PublishErrorCategory.ACTION_REQUIRED, "CAPABILITY_MISSING"), (failure.Category, failure.Code));
    }

    // ---- Facebook --------------------------------------------------------------------------------------

    [Fact]
    public async Task Facebook_fetches_a_page_token_then_posts_text_and_link_to_the_page_feed()
    {
        var handler = new StubHandler((request, _) => request.Method == HttpMethod.Get
            ? Json("""{"access_token":"page-token","id":"p1"}""")
            : Json("""{"id":"p1_777"}""", HttpStatusCode.OK, ("x-fb-trace-id", "trace-1")));
        var receipt = await new FacebookPublisher(Client(handler), Config).PublishAsync(Context(SocialPlatform.Facebook, "p1", ["publish_text"]), CancellationToken.None);

        Assert.Equal("https://graph.facebook.com/v21.0/p1?fields=access_token", handler.Requests[0].Request.RequestUri!.ToString());
        Assert.Equal("secret-user-token", handler.Requests[0].Request.Headers.Authorization!.Parameter);
        var (post, form) = handler.Requests[1];
        Assert.Equal("https://graph.facebook.com/v21.0/p1/feed", post.RequestUri!.ToString());
        Assert.Equal("page-token", post.Headers.Authorization!.Parameter);
        Assert.Contains("link=https%3A%2F%2Fbrightline.example%2Fdemo", form);
        Assert.DoesNotContain("token", form);
        Assert.Equal(("p1_777", "https://www.facebook.com/p1_777", "trace-1"), (receipt.ExternalId, receipt.ExternalUrl, receipt.ProviderRequestId));
    }

    [Theory]
    [InlineData("190", HttpStatusCode.BadRequest, PublishErrorCategory.ACTION_REQUIRED, "AUTH_EXPIRED")]
    [InlineData("4", HttpStatusCode.BadRequest, PublishErrorCategory.RETRYABLE, "THROTTLED")]
    [InlineData("200", HttpStatusCode.Forbidden, PublishErrorCategory.ACTION_REQUIRED, "PERMISSION_MISSING")]
    [InlineData("1", HttpStatusCode.InternalServerError, PublishErrorCategory.UNCERTAIN, "PROVIDER_ERROR_AFTER_SEND")]
    public async Task Facebook_graph_errors_on_the_create_call_are_normalized(string graphCode, HttpStatusCode status, PublishErrorCategory category, string code)
    {
        var handler = new StubHandler((request, _) => request.Method == HttpMethod.Get
            ? Json("""{"access_token":"page-token"}""")
            : Json("{\"error\":{\"message\":\"graph says no\",\"code\":" + graphCode + "}}", status));
        var failure = await Assert.ThrowsAsync<PublishFailure>(() =>
            new FacebookPublisher(Client(handler), Config).PublishAsync(Context(SocialPlatform.Facebook, "p1", ["publish_text"]), CancellationToken.None));
        Assert.Equal((category, code), (failure.Category, failure.Code));
    }

    [Fact]
    public async Task Facebook_server_error_while_fetching_the_page_token_is_safe_to_retry()
    {
        var handler = new StubHandler((_, _) => Json("""{"error":{"message":"down","code":2}}""", HttpStatusCode.ServiceUnavailable));
        var failure = await Assert.ThrowsAsync<PublishFailure>(() =>
            new FacebookPublisher(Client(handler), Config).PublishAsync(Context(SocialPlatform.Facebook, "p1", ["publish_text"]), CancellationToken.None));
        Assert.Equal((PublishErrorCategory.RETRYABLE, "PROVIDER_UNAVAILABLE"), (failure.Category, failure.Code));
        Assert.Single(handler.Requests); // nothing was posted
    }

    // ---- Instagram -------------------------------------------------------------------------------------

    [Fact]
    public async Task Instagram_runs_container_status_and_publish_as_separate_steps()
    {
        var handler = new StubHandler((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/v21.0/ig1/media" => Json("""{"id":"c-1"}"""),
            "/v21.0/c-1" => Json("""{"status_code":"IN_PROGRESS"}"""),
            "/v21.0/ig1/media_publish" => Json("""{"id":"m-9"}"""),
            _ => Json("""{"permalink":"https://www.instagram.com/p/xyz/"}"""),
        });
        var publisher = new InstagramPublisher(Client(handler), Config);
        var media = new PublishMedia(Guid.NewGuid(), "image/jpeg", new string('a', 64), "Alt", _ => Task.FromResult<Stream>(new MemoryStream()), "https://cdn.test/a.jpg");
        var context = Context(SocialPlatform.Instagram, "ig1", ["publish_image"], media);

        await publisher.ValidateCapabilityAsync(context, CancellationToken.None);
        var container = await publisher.PrepareAsync(context, CancellationToken.None);
        var status = await publisher.GetStatusAsync(context with { ContainerId = container }, CancellationToken.None);
        var receipt = await publisher.PublishAsync(context with { ContainerId = container }, CancellationToken.None);

        Assert.Equal(("c-1", MediaContainerStatus.InProgress), (container, status));
        Assert.Contains("image_url=https%3A%2F%2Fcdn.test%2Fa.jpg", handler.Requests[0].Body);
        Assert.Contains("creation_id=c-1", handler.Requests[2].Body);
        Assert.Equal(("m-9", "https://www.instagram.com/p/xyz/"), (receipt.ExternalId, receipt.ExternalUrl));
    }

    [Fact]
    public async Task Instagram_container_creation_timeout_is_retryable_but_publish_timeout_is_uncertain()
    {
        var publisher = new InstagramPublisher(Client(new StubHandler((_, _) => throw new TaskCanceledException("timeout"))), Config);
        var media = new PublishMedia(Guid.NewGuid(), "image/jpeg", new string('a', 64), null, _ => Task.FromResult<Stream>(new MemoryStream()), "https://cdn.test/a.jpg");
        var context = Context(SocialPlatform.Instagram, "ig1", ["publish_image"], media, container: "c-1");

        Assert.Equal(PublishErrorCategory.RETRYABLE, (await Assert.ThrowsAsync<PublishFailure>(() => publisher.PrepareAsync(context, CancellationToken.None))).Category);
        Assert.Equal(PublishErrorCategory.UNCERTAIN, (await Assert.ThrowsAsync<PublishFailure>(() => publisher.PublishAsync(context, CancellationToken.None))).Category);
    }

    [Fact]
    [Trait("Category", "Acceptance")]
    [Trait("Acceptance", "14 Instagram requires image")]
    public async Task Instagram_requires_an_image_reachable_by_a_public_url()
    {
        var publisher = new InstagramPublisher(Client(new StubHandler((_, _) => Json("{}"))), Config);
        var privateMedia = new PublishMedia(Guid.NewGuid(), "image/jpeg", new string('a', 64), null, _ => Task.FromResult<Stream>(new MemoryStream()), null);

        var noImage = await Assert.ThrowsAsync<PublishFailure>(() =>
            publisher.ValidateCapabilityAsync(Context(SocialPlatform.Instagram, "ig1", ["publish_image"]), CancellationToken.None));
        var noUrl = await Assert.ThrowsAsync<PublishFailure>(() =>
            publisher.ValidateCapabilityAsync(Context(SocialPlatform.Instagram, "ig1", ["publish_image"], privateMedia), CancellationToken.None));
        Assert.Equal(("MEDIA_MISSING", "MEDIA_URL_UNAVAILABLE"), (noImage.Code, noUrl.Code));
        Assert.All([noImage, noUrl], f => Assert.Equal(PublishErrorCategory.ACTION_REQUIRED, f.Category));
    }

    // ---- Simulation ------------------------------------------------------------------------------------

    [Fact]
    public async Task Simulation_serves_only_simulated_mode_and_returns_no_provider_evidence()
    {
        var simulated = new SimulatedPublisher();
        Assert.True(simulated.CanPublish(SocialPlatform.Instagram, ExecutionMode.SIMULATED));
        Assert.False(simulated.CanPublish(SocialPlatform.LinkedIn, ExecutionMode.LIVE));
        Assert.False(new LinkedInPublisher(Client(new StubHandler((_, _) => Json("{}"))), Config).CanPublish(SocialPlatform.LinkedIn, ExecutionMode.SIMULATED));

        var context = Context(SocialPlatform.LinkedIn, "", []) with { ExecutionMode = ExecutionMode.SIMULATED, AccessToken = null };
        var first = await simulated.PublishAsync(context, CancellationToken.None);
        var second = await simulated.PublishAsync(context, CancellationToken.None);
        Assert.Equal((null, null, null), (first.ExternalId, first.ExternalUrl, first.ProviderRequestId));
        Assert.Equal(first.Detail, second.Detail); // deterministic
    }
}
