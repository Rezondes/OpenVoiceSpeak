using System.Runtime.InteropServices;

namespace OVS.Client.Audio.Rust;

/// <summary>Package 175: a native jitter buffer's counts and measures, laid out as <c>JitterStats</c> in native/ovs_audio/src/core/jitter.rs.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeJitterStats
{
    /// <summary>Frames that arrived.</summary>
    public int Received;
    /// <summary>Frames that never arrived or were dropped as the buffer was full.</summary>
    public int Lost;
    /// <summary>Lost frames rebuilt from the next one's FEC.</summary>
    public int Rebuilt;
    /// <summary>Frames the decoder made up.</summary>
    public int Concealed;
    /// <summary>Missing frames played in full from the copy in the next packet.</summary>
    public int Recovered;
    /// <summary>Frames a talk spurt waits for.</summary>
    public int Cushion;
    /// <summary>What the buffer holds back, in ms.</summary>
    public int DelayMs;
    /// <summary>Frames waiting.</summary>
    public int Buffered;
    /// <summary>The interarrival jitter of RFC 3550 in ms.</summary>
    public double JitterMs;
    /// <summary>How late frames came lately, in ms.</summary>
    public double LateMs;
    /// <summary>1 when the last frame came over a link.</summary>
    public byte ViaLink;
    /// <summary>1 when frames wait or it is playing.</summary>
    public byte Busy;
}
