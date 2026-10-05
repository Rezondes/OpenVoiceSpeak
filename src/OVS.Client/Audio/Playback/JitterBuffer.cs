using System.Diagnostics;
using OVS.Client.Audio.Codec;
using OVS.Shared.Voice;

namespace OVS.Client.Audio.Playback;

/// <summary>
/// Reorders one speaker's Opus frames and hides losses. Pull() is called once per 20 ms tick.
/// The sender's sequence only advances while it transmits, so a talk pause shows up as an
/// underrun, never as a gap: after a short concealment we rebuffer and continue at the same seq.
/// Package 133 (A142): counts what happens to the frames and estimates the jitter; not thread-safe (the mixer locks).
/// </summary>
public sealed class JitterBuffer
{
    public const int StartDelay = 2;   // the least cushion before playback starts: 40 ms (Package 134: Cushion)
    public const int MaxFrames = 10;   // caps the latency at 200 ms
    const int MaxUnderruns = 2;        // then assume a talk pause and rebuffer
    public const int FrameMs = 20;

    readonly SortedDictionary<uint, byte[]> frames = [];
    readonly Dictionary<uint, byte[]> copies = []; // Package 136: frames still missing, as copied into the packet after them
    readonly VoiceDecoder decoder = new();
    bool started, playing;
    uint next;
    int waited, underruns;
    double lastTransit = double.NaN, lastArrival = double.NegativeInfinity;
    bool resumed; // Package 133: this spurt follows the last one closely, so frames missing between them were lost

    /// <summary>Package 133: frames that arrived, never came or were dropped as the buffer was full (Lost, some of them
    /// Rebuilt from the next one's FEC), or were made up by the decoder while waiting inside a talk spurt or for a lost one
    /// (Concealed).</summary>
    public int Received { get; private set; }
    public int Lost { get; private set; }
    public int Rebuilt { get; private set; }
    public int Concealed { get; private set; }

    /// <summary>Package 136 (A145): missing frames played in full from the copy in the next packet; neither Lost nor made up.</summary>
    public int Recovered { get; private set; }

    /// <summary>Package 133 (A142): the interarrival jitter of RFC 3550 in ms, from the arrival times against 20 ms per frame.</summary>
    public double JitterMs { get; private set; }

    /// <summary>Package 134 (review): how late frames came lately in ms, as a burst after a stall shows it; fades with a
    /// half-life of about 230 frames (4.6 s), so stalls every few seconds keep it up.</summary>
    public double LateMs { get; private set; }
    const double LateDecay = 0.997;
    double maxTransit = double.NaN;

    /// <summary>
    /// Package 134 (A143): frames a talk spurt (or a rebuffer inside one) waits for, from the measured jitter or lateness.
    /// It only sets where playback starts; frames already buffered are never dropped to follow it, as there is no time
    /// stretching.
    /// </summary>
    public int Cushion => Math.Clamp((int)Math.Ceiling(Math.Max(2 * JitterMs, LateMs) / FrameMs) + 1, StartDelay, MaxFrames);

    /// <summary>Package 133: what the buffer holds back right now.</summary>
    public int DelayMs => frames.Count * FrameMs;

    public bool ViaLink { get; private set; }
    public int Buffered => frames.Count;

    /// <summary>Package 124: frames waiting, or playing (concealing a loss counts): the next Pull may return audio.</summary>
    public bool Busy => playing || frames.Count > 0;

    /// <param name="arrivalMs">Package 133: when the frame arrived in ms, on any clock; now by default.</param>
    /// <param name="copy">Package 136 (A145): the frame before this one, kept only while that one is missing and still to play.</param>
    public void Push(uint seq, byte[] opus, bool viaLink, double? arrivalMs = null, byte[]? copy = null)
    {
        ViaLink = viaLink;
        if (frames.ContainsKey(seq)) return; // Package 136 (review): a duplicate is neither counted nor stored again
        Received++;
        double arrival = arrivalMs ?? NowMs();
        double transit = arrival - (double)FrameMs * seq;
        // a talk pause shifts the transit by its length (the frame number stood still), so there is a sample only while
        // playing: a frame that starts a spurt, also after a short pause, starts anew. A jump in the frame numbers while
        // playing (another route, a hostile sender) is no jitter either: more than the buffer holds is not a sample.
        bool idle = !playing && frames.Count == 0;
        bool close = arrival - lastArrival <= LossWindow.FreshAfter.TotalMilliseconds;
        if (!close)
        {
            // Package 134 (review): after a long silence one noisy spurt end must not hold back the next spurt fully
            JitterMs /= 2;
            LateMs /= 2;
        }
        double step = double.IsNaN(lastTransit) ? double.NaN : Math.Abs(transit - lastTransit);
        if (playing && step < MaxFrames * FrameMs) JitterMs += (step - JitterMs) / 16;
        lastTransit = transit;
        // Package 134 (review): a stall drains the buffer and its frames then come in a burst while rebuffering, which the
        // jitter above never sees. The burst shows it: the newest frames are early against the first, by as much as that
        // was late. Measured against the highest transit since the buffer last ran empty, so a talk pause (the transit
        // rises, the frame number stood still) is no lateness; a jump in the frame numbers starts anew, as above.
        double late = maxTransit - transit;
        LateMs *= LateDecay;
        if (!started || idle || !(late >= 0 && late < MaxFrames * FrameMs)) maxTransit = transit;
        else
        {
            LateMs = Math.Max(LateMs, late);
            maxTransit -= 1; // ms per frame, so a lasting drop in the latency fades out
        }
        // the same rule as LossWindow: after a silence of a second the frame numbers may have moved on through routing
        if (idle) resumed = close;
        lastArrival = arrival;
        if (started && seq < next) return; // too late, already played or concealed
        frames[seq] = opus;
        copies.Remove(seq); // what arrived itself wins over its copy
        if (copy is not null && seq > 0 && !(started && seq - 1 < next) && !frames.ContainsKey(seq - 1)) copies[seq - 1] = copy;
        bool dropped = false;
        while (frames.Count > MaxFrames)
        {
            var oldest = frames.Keys.First();
            frames.Remove(oldest);
            Lost++; // never played
            if (started && oldest >= next) next = oldest + 1;
            dropped = true;
        }
        if (dropped) DropPlayedCopies();
    }

    /// <summary>Package 136: copies of frames playback has moved past; no allocation (removing while enumerating is allowed).</summary>
    void DropPlayedCopies()
    {
        foreach (var key in copies.Keys)
            if (key < next) copies.Remove(key);
    }

    public static double NowMs() => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;

    /// <returns>20 ms of PCM, or null while buffering or idle.</returns>
    public float[]? Pull()
    {
        if (!playing)
        {
            if (frames.Count == 0) return null;
            int cushion = Cushion; // Package 134: fits the jitter; the jitter only moves while playing
            if (frames.Count < cushion && ++waited < cushion) return null;
            var first = frames.Keys.First();
            // Package 136 (review): a lost onset plays from its copy; nothing older, and nothing played already
            if (first > 0 && copies.ContainsKey(first - 1) && (!started || first - 1 >= next)) first--;
            // the end of the last spurt or the start of this one never came; a long silence or a big jump starts afresh
            if (started && resumed && first > next && first - next <= LossWindow.MaxGap) Lost += (int)(first - next);
            next = started ? Math.Max(next, first) : first;
            started = playing = true;
            waited = underruns = 0;
            DropPlayedCopies();
        }

        bool fromCopy = false;
        if (frames.Remove(next, out var opus) || (fromCopy = copies.Remove(next, out opus)))
        {
            if (fromCopy) Recovered++; // Package 136: played in full, so not lost
            Concealed += underruns; // waited inside the spurt; at its end they do not count, as a talk pause is no loss
            underruns = 0;
            next++;
            return decoder.Decode(opus);
        }
        if (frames.Count > 0)
        {
            // lost packet, later ones already here: the very next one carries an FEC copy of it (Package 136: also as a copy)
            Lost++;
            // Package 142: a music coder packet carries no FEC, so the frame is concealed (and counted so)
            if ((frames.TryGetValue(++next, out var following) || copies.TryGetValue(next, out following)) && VoiceDecoder.MayCarryFec(following))
            {
                Rebuilt++;
                return decoder.DecodeLost(following);
            }
            Concealed++;
            return decoder.Decode([]);
        }
        if (++underruns > MaxUnderruns)
        {
            playing = false;
            return null;
        }
        return decoder.Decode([]);
    }
}
