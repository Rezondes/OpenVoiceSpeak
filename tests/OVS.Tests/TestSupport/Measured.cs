namespace OVS.Tests.TestSupport;

/// <summary>One measurement a <see cref="MetricRecorder"/> got: the instrument's name, the value and its tags.</summary>
internal sealed record Measured(string Name, double Value, IReadOnlyDictionary<string, string> Tags);
