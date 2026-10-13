namespace OVS.Tests.TestSupport;

/// <summary>One line a <see cref="RecordingLoggerProvider"/> got: its logger category and its formatted text.</summary>
internal sealed record RecordedLine(string Category, string Text);
