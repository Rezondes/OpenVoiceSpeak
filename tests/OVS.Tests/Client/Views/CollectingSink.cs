using Avalonia.Logging;

namespace OVS.Tests.Client.Views;

/// <summary>Collects what Avalonia reports while windows load: missing resources and broken bindings do not throw.</summary>
sealed class CollectingSink : ILogSink
{
    public readonly List<string> Problems = [];
    public bool IsEnabled(LogEventLevel level, string area) => level >= LogEventLevel.Warning;
    public void Log(LogEventLevel level, string area, object? source, string messageTemplate) => Problems.Add($"{area}: {messageTemplate}");

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
    {
        // "Server.X ... Value is null" is by design while not connected: those bindings have a FallbackValue.
        if (propertyValues.LastOrDefault() is "Value is null.") return;
        Problems.Add($"{area}: {messageTemplate} [{string.Join(", ", propertyValues)}]");
    }
}
