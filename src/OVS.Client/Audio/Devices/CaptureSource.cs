using System.Diagnostics;
using OVS.Client.Audio.Capture;

namespace OVS.Client.Audio.Devices;

/// <summary>
/// Package 124: where the capture comes from, the microphone or, while one is set, a test tone in its place, and the
/// capture opened from there: opened, set up by the engine and started, and closed. It runs under the audio engine's
/// lock, which it is handed (a member marked "under gate" asserts that its caller holds it). When a capture opens, and
/// when the tone changes, is the <see cref="DeviceLifecycle"/>'s, which builds it.
/// </summary>
internal sealed class CaptureSource
{
    readonly object gate; // the engine's: the devices and settings
    readonly IAudioHardware? hardware;
    readonly Action<CapturePipeline> configure;
    CapturePipeline? capture;

    /// <param name="gate">The engine's lock over the devices and its settings; every open and close runs under it.</param>
    /// <param name="hardware">Package 124: where the microphone comes from; null runs without it (--no-audio, tests).</param>
    /// <param name="configure">
    /// Sets a new capture up before it starts: the engine's <c>ConfigureCapture</c> (see there for what it sets). Under gate.
    /// </param>
    internal CaptureSource(object gate, IAudioHardware? hardware, Action<CapturePipeline> configure)
    {
        this.gate = gate;
        this.hardware = hardware;
        this.configure = configure;
    }

    /// <summary>The open capture, or null; the playback thread reads it per frame for the echo canceller's far end.</summary>
    internal CapturePipeline? Capture => Volatile.Read(ref capture);

    /// <summary>
    /// Test tone frequency replacing the microphone, or null for the real microphone; set by
    /// <see cref="DeviceLifecycle.SetTone"/> under gate.
    /// </summary>
    public double? ToneHz { get; internal set; }

    /// <summary>Package 137: the microphone is the source (no tone set), so an open is a device open with its failure count.</summary>
    internal bool UsesMicrophone => ToneHz is null;

    /// <summary>
    /// Opens the capture from the source: the tone while one is set, else the chosen microphone (null: "Standard"; none
    /// without hardware), sets it up and starts it. A failure throws (a device that cannot open throws, IAudioHardware)
    /// and leaves what opened to <see cref="Close"/>. Under gate, with no capture open.
    /// </summary>
    /// <param name="inputId">The chosen microphone, null for "Standard".</param>
    /// <param name="opened">Takes the hardware's warnings of the open (the chosen device missing, the default opened).</param>
    internal void Open(string? inputId, List<string> opened)
    {
        Debug.Assert(Monitor.IsEntered(gate));
        Debug.Assert(capture is null);
        #pragma warning disable CA2000 // kept in the capture field; Close disposes it
        var opening = ToneHz is { } hz ? CapturePipeline.FromTone(hz) : hardware?.OpenInput(inputId, opened);
        #pragma warning restore CA2000
        if (opening is null) return;
        try
        {
            configure(opening);
        }
        finally
        {
            // published only once set up: the playback thread reads it outside the gate; a failed set-up is left to Close
            Volatile.Write(ref capture, opening);
        }
        opening.Start();
    }

    /// <summary>Under gate.</summary>
    internal void Close()
    {
        Debug.Assert(Monitor.IsEntered(gate));
        capture?.Dispose();
        capture = null;
    }
}
