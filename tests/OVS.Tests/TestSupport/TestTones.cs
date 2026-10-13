using OVS.Client.Audio.Codec;

namespace OVS.Tests.TestSupport;

/// <summary>Test audio the audio tests share.</summary>
static class TestTones
{
    /// <summary>One mono frame (20 ms) of a 440 Hz sine with <paramref name="amplitude"/>, from sample <paramref name="firstSample"/> of the tone.</summary>
    public static float[] SineFrame(int firstSample = 0, float amplitude = 0.5f) =>
        Enumerable.Range(firstSample, AudioFormat.FrameSamples)
            .Select(index => amplitude * MathF.Sin(2 * MathF.PI * 440 * index / AudioFormat.SampleRate)).ToArray();
}
