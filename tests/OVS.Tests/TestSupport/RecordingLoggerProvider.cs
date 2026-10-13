using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace OVS.Tests.TestSupport;

/// <summary>Package 144: a logger provider that keeps every line with its category and counts how often it was disposed.</summary>
internal sealed class RecordingLoggerProvider : ILoggerProvider
{
    int disposed;

    /// <summary>Every line logged through it, from any thread.</summary>
    public ConcurrentQueue<RecordedLine> Lines { get; } = new();

    /// <summary>How often Dispose ran.</summary>
    public int Disposed => Volatile.Read(ref disposed);

    /// <summary>
    /// Registers it so the logger factory's container hands it out and disposes it with the factory, like the providers
    /// the OpenTelemetry SDK registers (an instance given to AddProvider is never disposed by the factory).
    /// </summary>
    public void AddTo(ILoggingBuilder builder) => builder.Services.AddSingleton<ILoggerProvider>(_ => this);

    public ILogger CreateLogger(string categoryName) => new Recorder(this, categoryName);

    public void Dispose() => Interlocked.Increment(ref disposed);

    sealed class Recorder(RecordingLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            owner.Lines.Enqueue(new(category, formatter(state, exception)));
    }
}
