using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace SocialMediaAutomation.IntegrationTests.Infrastructure;

/// <summary>Keeps every log line the host writes (message, structured values, scopes, exception) so tests can search them.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private IExternalScopeProvider scopes = new LoggerExternalScopeProvider();

    public ConcurrentQueue<string> Lines { get; } = new();

    public string All => string.Join('\n', Lines);

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => scopes = scopeProvider;

    public void Dispose()
    {
    }

    private sealed class Logger(CapturingLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => owner.scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var scopeText = new List<string>();
            owner.scopes.ForEachScope((scope, list) => list.Add(Describe(scope)), scopeText);
            owner.Lines.Enqueue($"{logLevel} {category}: {formatter(state, exception)} | {Describe(state)} | {string.Join(" ; ", scopeText)} | {exception}");
        }

        private static string Describe(object? value) => value is IEnumerable<KeyValuePair<string, object?>> pairs
            ? string.Join(", ", pairs.Select(p => $"{p.Key}={p.Value}"))
            : value?.ToString() ?? "";
    }
}
