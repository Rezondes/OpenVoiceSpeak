using Microsoft.Extensions.Logging;

namespace OVS.Tests.TestSupport;

/// <summary>An <see cref="ILogger"/> that adds each formatted message to <paramref name="lines"/>.</summary>
internal sealed class LineLogger(List<string> lines) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        lines.Add(formatter(state, exception));
}
