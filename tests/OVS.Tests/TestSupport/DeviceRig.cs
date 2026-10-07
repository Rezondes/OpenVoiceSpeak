using OVS.Client.Audio;
using OVS.Client.Audio.Devices;
using OVS.Client.Input;
using OVS.Client.Settings;
using OVS.Tests.Client.Audio.Transmit;

namespace OVS.Tests.TestSupport;

/// <summary>
/// Package 137 (A146): an engine on a manual clock with fake devices; the device changes are raised by hand and the
/// reopen runs on the test thread when the clock passes the debounce.
/// </summary>
internal sealed class DeviceRig : IDisposable
{
    public readonly FakeAudioHardware Hardware = new();
    public readonly ManualTimeProvider Time = new();
    public readonly KeyPoller Keys;
    public readonly AudioEngine Engine;
    readonly List<string> notices = [];

    public DeviceRig(ClientSettings settings, params (string Id, AudioDirection Direction)[] present)
    {
        foreach (var (id, direction) in present) Hardware.Present.Add(id, direction);
        Keys = new KeyPoller(Time);
        Engine = new AudioEngine(Keys, Time, hardware: Hardware);
        Engine.Warning += text =>
        {
            lock (notices) notices.Add(text);
        };
        Engine.Configure(settings);
    }

    public List<string> Notices
    {
        get { lock (notices) return [.. notices]; }
    }

    /// <summary>Connects and waits until microphone and speaker are open (they open off the calling thread).</summary>
    public async Task ConnectAsync()
    {
        Engine.Connected = true;
        await SendPathTests.Until(() => Hardware.Counts is { MicsOpen: 1, SpeakersOpen: 1 });
        Assert.Equal((1, 1, 1, 1), Hardware.Counts);
    }

    /// <summary>A device change, and the quiet time after it runs out.</summary>
    public void Change()
    {
        Hardware.RaiseChanged();
        Time.Advance(AudioEngine.DeviceSettle);
    }

    public void Dispose()
    {
        Engine.Dispose();
        Keys.Dispose();
    }
}
