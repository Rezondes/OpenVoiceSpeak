using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace OVS.Tests.TestSupport;

/// <summary>Package 147: listens to exactly one meter (one run's) and keeps every measurement with its tags.</summary>
internal sealed class MetricRecorder : IDisposable
{
    readonly MeterListener listener = new();

    public MetricRecorder(Meter meter)
    {
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter == meter) meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Add(instrument, value, tags));
        listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) => Add(instrument, value, tags));
        listener.Start();
    }

    /// <summary>Every measurement so far.</summary>
    public ConcurrentQueue<Measured> Measurements { get; } = new();

    void Add(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var copy = new Dictionary<string, string>();
        foreach (var (key, tagValue) in tags) copy[key] = tagValue?.ToString() ?? "";
        Measurements.Enqueue(new(instrument.Name, value, copy));
    }

    /// <summary>Reads the observable instruments (the sessions online) once.</summary>
    public void Collect() => listener.RecordObservableInstruments();

    /// <summary>The measurements of <paramref name="name"/> whose tags include all of <paramref name="tags"/>.</summary>
    public List<Measured> Of(string name, params (string Key, string Value)[] tags) =>
        [.. Measurements.Where(measured => measured.Name == name && tags.All(tag => measured.Tags.GetValueOrDefault(tag.Key) == tag.Value))];

    /// <summary>The sum of those measurements.</summary>
    public double Sum(string name, params (string Key, string Value)[] tags) => Of(name, tags).Sum(measured => measured.Value);

    public void Dispose() => listener.Dispose();
}
