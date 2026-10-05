using SocialMediaAutomation.Application.Abstractions;

namespace SocialMediaAutomation.IntegrationTests.Infrastructure;

/// <summary>Scripted stand-in for the hosted LLM. Tests set <see cref="Handler"/>; every call is recorded.</summary>
public sealed class FakeLlmClient : ILlmClient
{
    public const string EvaluatorMarker = "strict brand and compliance reviewer";

    public Func<LlmConnection, LlmChatRequest, LlmChatResponse> Handler { get; set; } =
        (_, _) => throw new InvalidOperationException("No LLM response scripted for this test.");

    public IReadOnlyList<string> Models { get; set; } = ["gpt-test-mini", "gpt-test"];

    public List<(LlmConnection Connection, LlmChatRequest Request)> Calls { get; } = [];

    public Task<LlmChatResponse> CompleteJsonAsync(LlmConnection connection, LlmChatRequest request, CancellationToken cancellationToken)
    {
        lock (Calls)
            Calls.Add((connection, request));
        return Task.FromResult(Handler(connection, request));
    }

    public Task<IReadOnlyList<string>> ListModelsAsync(LlmConnection connection, CancellationToken cancellationToken)
    {
        lock (Calls)
            Calls.Add((connection, new LlmChatRequest("models", "", 0, 0)));
        return Task.FromResult(Models);
    }

    public static bool IsEvaluation(LlmChatRequest request) => request.SystemPrompt.Contains(EvaluatorMarker, StringComparison.Ordinal);

    public static LlmChatResponse Reply(string json) => new(json, "gpt-test", 1200, 450, 850, 200);
}
