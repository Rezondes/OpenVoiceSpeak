using OVS.Client.Audio;
using OVS.Client.Audio.Transmit;
using OVS.Shared.Voice;

namespace OVS.Tests.TestSupport;

/// <summary>Reading and driving the audio engine and its loss adapter the same way in every test.</summary>
static class EngineProbe
{
    /// <summary>The engine's five switches that follow the connection.</summary>
    public static EngineSwitches ReadEngineSwitches(AudioEngine engine) =>
        new(engine.Connected, engine.SelfMuted, engine.Deafened, engine.HasSpeakLinked, engine.ChannelMuted);

    /// <summary>6 s of 20 % loss on <paramref name="clock"/>: a bitrate of 64 kbps halves to 32.</summary>
    public static void LowerByLoss(LossAdapter adapter, ManualTimeProvider clock)
    {
        for (int step = 0; step < 4; step++)
        {
            adapter.Report(new LossReport(100, 20));
            clock.Advance(TimeSpan.FromSeconds(2));
        }
    }
}
