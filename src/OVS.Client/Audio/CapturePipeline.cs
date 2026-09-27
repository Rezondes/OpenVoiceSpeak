using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace OVS.Client.Audio;

/// <summary>Microphone -> mono 48 kHz -> 20 ms frames -> VAD -> Opus. Windows only (WASAPI).</summary>
public sealed class CapturePipeline : IDisposable
{
    readonly FrameChunker chunker = new();
    readonly VoiceEncoder encoder = new();
    readonly object gate = new();
    LinearResampler? resampler;
    int resamplerRate;
    WasapiCapture? capture;

    CapturePipeline() { }

    public static CapturePipeline FromDevice(MMDevice? device)
    {
        var pipeline = new CapturePipeline();
        var capture = device is null ? new WasapiCapture() : new WasapiCapture(device);
        capture.DataAvailable += (_, e) =>
            pipeline.Feed(ToMono(e.Buffer, e.BytesRecorded, capture.WaveFormat), capture.WaveFormat.SampleRate);
        pipeline.capture = capture;
        return pipeline;
    }

    public VoiceActivityDetector Vad { get; } = new();
    public float Gain { get; set; } = 1f;

    /// <summary>Called per frame with the VAD result; returns the target or null to stay silent.</summary>
    public Func<bool, byte?> DecideTarget { get; set; } = _ => null;

    public event Action<byte[], byte>? FrameEncoded;
    public event Action<float>? Level;

    public void Start()
    {
        capture?.StartRecording();
    }

    public void Feed(ReadOnlySpan<float> mono, int sampleRate)
    {
        lock (gate)
        {
            if (resampler is null || resamplerRate != sampleRate)
            {
                resampler = new LinearResampler(sampleRate, AudioFormat.SampleRate);
                resamplerRate = sampleRate;
            }
            foreach (var frame in chunker.Add(resampler.Process(mono)))
            {
                float gain = Gain;
                for (int i = 0; i < frame.Length; i++) frame[i] = Math.Clamp(frame[i] * gain, -1f, 1f);
                bool voice = Vad.Process(frame);
                Level?.Invoke(Vad.LastLevelDb);
                if (DecideTarget(voice) is { } target) FrameEncoded?.Invoke(encoder.Encode(frame), target);
            }
        }
    }

    public static float[] ToMono(byte[] buffer, int bytes, WaveFormat format)
    {
        int channels = format.Channels;
        float[] interleaved;
        if (format.Encoding == WaveFormatEncoding.IeeeFloat || (format is WaveFormatExtensible ext && ext.SubFormat == NAudio.MediaFoundation.AudioSubtypes.MFAudioFormat_Float))
        {
            interleaved = MemoryMarshal.Cast<byte, float>(buffer.AsSpan(0, bytes)).ToArray();
        }
        else if (format.BitsPerSample == 16)
        {
            var shorts = MemoryMarshal.Cast<byte, short>(buffer.AsSpan(0, bytes));
            interleaved = new float[shorts.Length];
            for (int i = 0; i < shorts.Length; i++) interleaved[i] = shorts[i] / 32768f;
        }
        else
        {
            throw new NotSupportedException($"Aufnahmeformat {format} wird nicht unterstützt");
        }

        if (channels == 1) return interleaved;
        var mono = new float[interleaved.Length / channels];
        for (int i = 0; i < mono.Length; i++)
        {
            float sum = 0;
            for (int c = 0; c < channels; c++) sum += interleaved[i * channels + c];
            mono[i] = sum / channels;
        }
        return mono;
    }

    public void Dispose()
    {
        if (capture is not null)
        {
            capture.StopRecording();
            capture.Dispose();
        }
    }
}
