using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Infrastructure.Ai;

namespace SocialMediaAutomation.IntegrationTests;

/// <summary>The provider adapter against a stub HTTP endpoint (no real provider is called).</summary>
public class OpenAiCompatibleClientTests
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

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static readonly LlmConnection Groq = new(AiProvider.Groq, "https://api.groq.com/openai/v1", "llama-test", "gsk_secret_value");
    private static readonly LlmChatRequest Prompt = new("system text", "user text", 0.7, 500);

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Sends_a_json_mode_chat_completion_and_reads_content_and_usage()
    {
        var handler = new StubHandler((_, _) => Json(HttpStatusCode.OK,
            """{"model":"llama-test-0301","choices":[{"message":{"content":"{\"ok\":true}"}}],"usage":{"prompt_tokens":321,"completion_tokens":54}}"""));

        var response = await new OpenAiCompatibleClient(new Factory(handler)).CompleteJsonAsync(Groq, Prompt, CancellationToken.None);

        var (request, body) = Assert.Single(handler.Requests);
        var json = JsonNode.Parse(body!)!;
        Assert.Equal("https://api.groq.com/openai/v1/chat/completions", request.RequestUri!.ToString());
        Assert.Equal(("Bearer", "gsk_secret_value"), (request.Headers.Authorization!.Scheme, request.Headers.Authorization.Parameter));
        Assert.Equal("json_object", json["response_format"]!["type"]!.GetValue<string>());
        Assert.Equal(("llama-test", "system", "user text"), (json["model"]!.GetValue<string>(), json["messages"]![0]!["role"]!.GetValue<string>(),
            json["messages"]![1]!["content"]!.GetValue<string>()));
        Assert.DoesNotContain("gsk_secret_value", body); // the key travels only in the Authorization header
        Assert.Equal(("{\"ok\":true}", 321, 54, "llama-test-0301"), (response.Content, response.InputTokens, response.OutputTokens, response.ResponseModel));
        Assert.DoesNotContain("gsk_secret_value", Groq.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, LlmErrorKind.Authentication)]
    [InlineData(HttpStatusCode.TooManyRequests, LlmErrorKind.Transient)]
    [InlineData(HttpStatusCode.BadGateway, LlmErrorKind.Transient)]
    [InlineData(HttpStatusCode.NotFound, LlmErrorKind.Rejected)]
    public async Task Classifies_provider_errors(HttpStatusCode status, LlmErrorKind expected)
    {
        var handler = new StubHandler((_, _) => Json(status, """{"error":{"message":"model llama-test does not exist"}}"""));

        var error = await Assert.ThrowsAsync<LlmException>(() => new OpenAiCompatibleClient(new Factory(handler)).CompleteJsonAsync(Groq, Prompt, CancellationToken.None));

        Assert.Equal((expected, (int)status), (error.Kind, error.HttpStatus));
    }

    [Fact]
    public async Task Network_failure_is_transient()
    {
        var handler = new StubHandler((_, _) => throw new HttpRequestException("connection refused"));

        var error = await Assert.ThrowsAsync<LlmException>(() => new OpenAiCompatibleClient(new Factory(handler)).CompleteJsonAsync(Groq, Prompt, CancellationToken.None));

        Assert.Equal(LlmErrorKind.Transient, error.Kind);
    }

    [Fact]
    public async Task Retries_once_without_temperature_for_models_that_reject_it()
    {
        var handler = new StubHandler((_, body) => body!.Contains("temperature")
            ? Json(HttpStatusCode.BadRequest, """{"error":{"message":"Unsupported value: 'temperature' does not support 0.7"}}""")
            : Json(HttpStatusCode.OK, """{"choices":[{"message":{"content":"{}"}}]}"""));

        var response = await new OpenAiCompatibleClient(new Factory(handler)).CompleteJsonAsync(Groq, Prompt, CancellationToken.None);

        Assert.Equal("{}", response.Content);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Lists_model_ids()
    {
        var handler = new StubHandler((_, _) => Json(HttpStatusCode.OK, """{"data":[{"id":"b-model"},{"id":"a-model"},{"id":"a-model"}]}"""));

        var models = await new OpenAiCompatibleClient(new Factory(handler)).ListModelsAsync(Groq, CancellationToken.None);

        Assert.Equal(["b-model", "a-model"], models);
        Assert.Equal("https://api.groq.com/openai/v1/models", handler.Requests[0].Request.RequestUri!.ToString());
    }
}
