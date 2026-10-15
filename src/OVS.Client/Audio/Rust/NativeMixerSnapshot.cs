using System.Runtime.InteropServices;

namespace OVS.Client.Audio.Rust;

/// <summary>Package 176: what the client reads of the native mixer, laid out as <c>MixerSnapshot</c> in native/ovs_audio/src/voice.rs.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeMixerSnapshot
{
    /// <summary>The most speakers the mixer holds (<c>POOL</c>).</summary>
    public const int Pool = 64;

    /// <summary>The counts of every speaker so far, the worst jitter and delay of those present.</summary>
    public NativeJitterStats Stats;
    /// <summary>Frames mixed so far.</summary>
    public ulong Mixes;
    /// <summary>The voices' level in the last frame, in dB (louder side).</summary>
    public float LevelDb;
    /// <summary>Speakers heard in the last frame.</summary>
    public uint ActiveCount;
    /// <summary>Speakers with a buffer.</summary>
    public uint SpeakerCount;
    /// <summary>1 when something is left to play or to conceal.</summary>
    public byte Busy;
    /// <summary>The session ids heard in the last frame.</summary>
    public fixed uint ActiveIds[Pool];
    /// <summary>1 where that speaker came in over a link.</summary>
    public fixed byte ActiveLinks[Pool];
    /// <summary>The session ids with a buffer.</summary>
    public fixed uint SpeakerIds[Pool];
    /// <summary>A number per buffer, new for each buffer made: the same number means the same buffer kept.</summary>
    public fixed ulong SpeakerSerials[Pool];
}
