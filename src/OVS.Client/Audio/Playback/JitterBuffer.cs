using OVS.Client.Audio.Codec;
using OVS.Client.Audio.Rust;

namespace OVS.Client.Audio.Playback;

/// <summary>
/// Reorders one speaker's Opus frames and hides losses. Pull() is called once per 20 ms tick.
/// The sender's sequence only advances while it transmits, so a talk pause shows up as an
/// underrun, never as a gap: after a short concealment we rebuffer and continue at the same seq.
/// Package 133 (A142): counts what happens to the frames and estimates the jitter; not thread-safe (the mixer locks).
/// Package 175: the rules run in the native library (native/ovs_audio, <c>core/jitter.rs</c>) with libopus; this is
/// its managed face with the members it always had, for the mixer and the tests.
/// </summary>
internal sealed unsafe class JitterBuffer
{
    /// <summary>The least cushion before playback starts, in frames: 40 ms (Package 134: Cushion).</summary>
    public const int StartDelay = 2;
    /// <summary>The most frames held; caps the latency at 200 ms.</summary>
    public const int MaxFrames = 10;
    /// <summary>One frame's length in ms.</summary>
    public const int FrameMs = 20;

    readonly NativeAudio native = NativeAudio.Load();
    readonly NativeObjectHandle buffer;
    readonly float[] pcm = new float[AudioFormat.StereoFrameSamples]; // Package 124: reused by every pull

    /// <summary>A new, empty buffer; throws when the native library is unusable (A180).</summary>
    public JitterBuffer() => buffer = NativeObjectHandle.Create(native, native.JitterCreate, native.JitterFree);

    /// <summary>Package 175: whether buffers can be made at all (the native library loaded).</summary>
    public static bool Available => NativeAudio.Load().Error is null;

    NativeJitterStats Stats()
    {
        NativeJitterStats stats;
        native.JitterStats(buffer.Pointer, &stats);
        return stats;
    }

    /// <summary>Package 133: frames that arrived, never came or were dropped as the buffer was full (Lost, some of them
    /// Rebuilt from the next one's FEC), or were made up by the decoder while waiting inside a talk spurt or for a lost one
    /// (Concealed).</summary>
    public int Received => Stats().Received;
    /// <summary>Frames that never arrived.</summary>
    public int Lost => Stats().Lost;
    /// <summary>Lost frames played in full from the copy in the next packet (Package 136).</summary>
    public int Rebuilt => Stats().Rebuilt;
    /// <summary>Frames the decoder concealed.</summary>
    public int Concealed => Stats().Concealed;

    /// <summary>Package 136 (A145): missing frames played in full from the copy in the next packet; neither Lost nor made up.</summary>
    public int Recovered => Stats().Recovered;

    /// <summary>Package 133 (A142): the interarrival jitter of RFC 3550 in ms, from the arrival times against 20 ms per frame.</summary>
    public double JitterMs => Stats().JitterMs;

    /// <summary>Package 134 (review): how late frames came lately in ms, as a burst after a stall shows it; fades with a
    /// half-life of about 230 frames (4.6 s), so stalls every few seconds keep it up.</summary>
    public double LateMs => Stats().LateMs;

    /// <summary>
    /// Package 134 (A143): frames a talk spurt (or a rebuffer inside one) waits for, from the measured jitter or lateness.
    /// It only sets where playback starts; frames already buffered are never dropped to follow it, as there is no time
    /// stretching.
    /// </summary>
    public int Cushion => Stats().Cushion;

    /// <summary>Package 133: what the buffer holds back right now.</summary>
    public int DelayMs => Stats().DelayMs;

    /// <summary>The last frame came in over a link.</summary>
    public bool ViaLink => Stats().ViaLink != 0;
    /// <summary>Frames waiting.</summary>
    public int Buffered => Stats().Buffered;

    /// <summary>Package 124: frames waiting, or playing (concealing a loss counts): the next Pull may return audio.</summary>
    public bool Busy => Stats().Busy != 0;

    /// <param name="arrivalMs">Package 133: when the frame arrived in ms, on any clock (the mixer stamps it as it comes).</param>
    /// <param name="copy">Package 136 (A145): the frame before this one, kept only while that one is missing and still to play.</param>
    /// <param name="seq">The frame number.</param>
    /// <param name="opus">The Opus frame.</param>
    /// <param name="viaLink">It came in over a link.</param>
    public void Push(uint seq, byte[] opus, bool viaLink, double arrivalMs, byte[]? copy = null)
    {
        fixed (byte* data = opus)
        fixed (byte* copied = copy)
            native.JitterPush(buffer.Pointer, seq, data, (uint)opus.Length, viaLink ? (byte)1 : (byte)0, arrivalMs, copied, (uint)(copy?.Length ?? 0));
    }

    /// <returns>20 ms of PCM (Package 142: interleaved stereo, in a buffer reused by the next pull), or null while buffering or idle.</returns>
    public float[]? Pull()
    {
        fixed (float* frame = pcm) return native.JitterPull(buffer.Pointer, frame) != 0 ? pcm : null;
    }
}
