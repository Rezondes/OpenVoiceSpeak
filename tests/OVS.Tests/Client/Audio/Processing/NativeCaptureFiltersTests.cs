using System.Reflection;
using OVS.Client.Audio.Capture;
using OVS.Client.Audio.Processing;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Audio.Processing;

/// <summary>
/// Package 123 (noise suppression) and 140 (echo cancellation): the native filters a new capture gets, each loaded
/// while its library loads and given up with one log line when it does not; the voice then goes out unfiltered.
/// </summary>
public sealed class NativeCaptureFiltersTests
{
    /// <summary>A noise suppressor library that does not exist (shared by the tests of a missing library).</summary>
    internal const string NoNoiseLibrary = "ovs-no-such-rnnoise";

    /// <summary>An echo canceller library that does not exist.</summary>
    internal const string NoEchoLibrary = "ovs-no-such-aec";

    const int EchoDelay = 75; // any hint: the filters pass on what they are given

    readonly List<string> log = [];

    /// <summary>Every property of the pipeline that holds a filter of this folder (the noise suppressor, the echo canceller, any later one).</summary>
    static List<PropertyInfo> FilterProperties() =>
        [.. typeof(CapturePipeline).GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(property => property.PropertyType.Namespace == typeof(NativeCaptureFilters).Namespace)];

    /// <summary>
    /// The completeness check: a native filter is a pipeline property whose type lies in the Processing namespace; each one
    /// must be attached, so a filter added to the pipeline later fails here until it is attached too.
    /// </summary>
    [Fact]
    public void AttachTo_LibrariesPresent_AttachesEveryNativeFilter()
    {
        using var capture = CapturePipeline.Manual();

        new NativeCaptureFilters(new LineLogger(log)).AttachTo(capture, EchoDelay);

        var properties = FilterProperties();
        Assert.Contains(properties, property => property.Name == nameof(CapturePipeline.Suppressor));
        Assert.Contains(properties, property => property.Name == nameof(CapturePipeline.Canceller));
        Assert.All(properties, property => Assert.True(property.GetValue(capture) is not null, $"{property.Name} not attached"));
        Assert.Empty(log);
    }

    [Fact]
    public void AttachTo_LibrariesPresent_CancellerStartsFromTheGivenEchoDelay()
    {
        using var capture = CapturePipeline.Manual();

        new NativeCaptureFilters(new LineLogger(log)).AttachTo(capture, EchoDelay);

        Assert.Equal(EchoDelay, capture.Canceller!.DelayMs);
    }

    [Fact]
    public void AttachTo_LibrariesMissing_NoFiltersAndOneLogLineEach()
    {
        var filters = new NativeCaptureFilters(new LineLogger(log), NoNoiseLibrary, NoEchoLibrary);
        using var first = CapturePipeline.Manual();
        using var second = CapturePipeline.Manual();

        filters.AttachTo(first, EchoDelay);
        filters.AttachTo(second, EchoDelay); // the second capture logs nothing more: a missing library is not tried again

        Assert.Null(second.Suppressor);
        Assert.Null(second.Canceller);
        Assert.Equal(2, log.Count);
        Assert.StartsWith($"Noise suppression off: {NoNoiseLibrary} cannot be loaded (", log[0]);
        Assert.StartsWith($"Echo cancellation off: {NoEchoLibrary} cannot be loaded (", log[1]);
    }

    [Fact]
    public void AttachTo_OnlyNoiseLibraryMissing_CancellerStillAttached()
    {
        var filters = new NativeCaptureFilters(new LineLogger(log), noiseLibrary: NoNoiseLibrary);
        using var first = CapturePipeline.Manual();
        using var second = CapturePipeline.Manual();

        filters.AttachTo(first, EchoDelay);
        filters.AttachTo(second, EchoDelay);

        Assert.Null(second.Suppressor);
        Assert.NotNull(second.Canceller);
        Assert.Contains(NoNoiseLibrary, Assert.Single(log));
    }

    [Fact]
    public void AttachTo_OnlyEchoLibraryMissing_SuppressorStillAttached()
    {
        var filters = new NativeCaptureFilters(new LineLogger(log), echoLibrary: NoEchoLibrary);
        using var first = CapturePipeline.Manual();
        using var second = CapturePipeline.Manual();

        filters.AttachTo(first, EchoDelay);
        filters.AttachTo(second, EchoDelay);

        Assert.True(second.Suppressor is not null, "the noise suppressor turned off with the echo canceller");
        Assert.Null(second.Canceller);
        Assert.Contains(NoEchoLibrary, Assert.Single(log));
    }
}
