using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Infrastructure.Ai;

namespace SocialMediaAutomation.IntegrationTests;

/// <summary>The Hugging Face text-to-image adapter against a stub HTTP endpoint (no real provider is called).</summary>
public class HuggingFaceImageClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request, request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            return respond(request);
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static readonly ImageConnection Connection = new(AiProvider.HuggingFace,
        AiProviderSettings.ResolveBaseUrl(AiProvider.HuggingFace, null), "Tongyi-MAI/Z-Image-Turbo", "hf_secret_value", "1536x1024");

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private const string Mapping =
        """{"inferenceProviderMapping":{"fal-ai":{"status":"live","providerId":"fal-ai/z-image/turbo","task":"text-to-image"}}}""";

    [Fact]
    public async Task Resolves_the_provider_model_and_returns_the_inline_jpeg()
    {
        var handler = new StubHandler(r => r.RequestUri!.Host == "huggingface.co"
            ? Json(HttpStatusCode.OK, Mapping)
            : Json(HttpStatusCode.OK, $$"""{"images":[{"url":"data:image/jpeg;base64,{{Convert.ToBase64String(Jpeg)}}"}]}"""));

        var image = await new HuggingFaceImageClient(new Factory(handler)).GenerateAsync(Connection, "a sunrise", CancellationToken.None);

        Assert.Equal(Jpeg, image.Bytes);
        var (request, body) = handler.Requests[1];
        Assert.Equal("https://router.huggingface.co/fal-ai/fal-ai/z-image/turbo", request.RequestUri!.ToString());
        Assert.Equal(("Bearer", "hf_secret_value"), (request.Headers.Authorization!.Scheme, request.Headers.Authorization.Parameter));
        var json = JsonNode.Parse(body!)!;
        Assert.Equal(("a sunrise", "jpeg", 1536, 1024), (json["prompt"]!.GetValue<string>(), json["output_format"]!.GetValue<string>(),
            json["image_size"]!["width"]!.GetValue<int>(), json["image_size"]!["height"]!.GetValue<int>()));
        Assert.DoesNotContain("hf_secret_value", body);
        Assert.DoesNotContain("hf_secret_value", Connection.ToString());
    }

    [Fact]
    public async Task A_hosted_result_is_downloaded_without_the_token()
    {
        var handler = new StubHandler(r => r.RequestUri!.Host switch
        {
            "huggingface.co" => Json(HttpStatusCode.OK, Mapping),
            "router.huggingface.co" => Json(HttpStatusCode.OK, """{"images":[{"url":"https://cdn.example/img.jpg"}]}"""),
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Jpeg) },
        });

        var image = await new HuggingFaceImageClient(new Factory(handler)).GenerateAsync(Connection, "p", CancellationToken.None);

        Assert.Equal(Jpeg, image.Bytes);
        Assert.Null(handler.Requests[2].Request.Headers.Authorization);
    }

    [Fact]
    public async Task A_model_not_served_for_text_to_image_is_rejected_without_generating()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"inferenceProviderMapping":{}}"""));

        var error = await Assert.ThrowsAsync<LlmException>(() =>
            new HuggingFaceImageClient(new Factory(handler)).GenerateAsync(Connection, "p", CancellationToken.None));

        Assert.Equal(LlmErrorKind.Rejected, error.Kind);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, LlmErrorKind.Authentication)]
    [InlineData(HttpStatusCode.TooManyRequests, LlmErrorKind.Transient)]
    [InlineData(HttpStatusCode.BadRequest, LlmErrorKind.Rejected)]
    public async Task Classifies_generation_errors(HttpStatusCode status, LlmErrorKind expected)
    {
        var handler = new StubHandler(r => r.RequestUri!.Host == "huggingface.co"
            ? Json(HttpStatusCode.OK, Mapping)
            : Json(status, """{"error":"nope"}"""));

        var error = await Assert.ThrowsAsync<LlmException>(() =>
            new HuggingFaceImageClient(new Factory(handler)).GenerateAsync(Connection, "p", CancellationToken.None));

        Assert.Equal(expected, error.Kind);
    }

    [Fact]
    public async Task Test_connection_checks_the_token_then_the_model()
    {
        var bad = new StubHandler(_ => Json(HttpStatusCode.Unauthorized, """{"error":"Invalid credentials"}"""));
        Assert.Equal(LlmErrorKind.Authentication, (await Assert.ThrowsAsync<LlmException>(() =>
            new HuggingFaceImageClient(new Factory(bad)).IsModelAvailableAsync(Connection, CancellationToken.None))).Kind);

        var good = new StubHandler(r => Json(HttpStatusCode.OK, r.RequestUri!.AbsolutePath.EndsWith("whoami-v2") ? """{"name":"x"}""" : Mapping));
        Assert.True(await new HuggingFaceImageClient(new Factory(good)).IsModelAvailableAsync(Connection, CancellationToken.None));
        Assert.False(await new HuggingFaceImageClient(new Factory(good)).IsModelAvailableAsync(Connection with { Model = "../evil" }, CancellationToken.None));
    }
}
