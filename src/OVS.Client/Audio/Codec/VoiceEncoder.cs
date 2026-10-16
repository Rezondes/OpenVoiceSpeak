using OVS.Client.Audio.Rust;
using OVS.Client.Audio.Transmit;
using OVS.Shared.Protocol;
using OVS.Shared.Voice;

namespace OVS.Client.Audio.Codec;

/// <summary>
/// Encodes 20 ms frames with Opus at the channel's bitrate. Package 179 (A183): libopus in the native library
/// (native/ovs_audio, <c>core/encoder.rs</c>), set up through Opus' own requests as Concentus was before.
/// </summary>
internal sealed unsafe class VoiceEncoder
{
    /// <summary>
    /// Package 124: Opus defaults to its most expensive setting. Measured on a speech-like signal at 32 kbps (PLAN.md,
    /// Package 124): 5 encodes in about 60 % of the time of 10 with the same spectral distance. It is the lowest value
    /// that keeps the voice part's spectral interpolation and warped noise shaping (from 4) and the music part's pitch
    /// pre-filter (from 5); what dropping them costs a real voice a synthetic signal cannot show.
    /// </summary>
    public const int Complexity = 5;

    /// <summary>Package 142 (A151): music goes out in stereo from this bitrate on (Fluxer's rule), below it in mono.</summary>
    public const int StereoFromKbps = 64;

    // Opus' request numbers (opus_defines.h)
    const int SetBitrate = 4002, GetBitrate = 4003, SetComplexity = 4010, GetComplexity = 4011, SetInbandFec = 4012,
        SetPacketLoss = 4014, GetPacketLoss = 4015, SetDtx = 4016, GetDtx = 4017, SetForceChannels = 4022, GetForceChannels = 4023,
        SetSignal = 4024, SignalVoice = 3001, SignalMusic = 3002, ResetStateRequest = 4028;

    readonly NativeAudio native = NativeAudio.Load();
    readonly NativeObjectHandle encoder;
    readonly byte[] buffer = new byte[VoiceHeader.MaxOpusSize];

    /// <param name="music">
    /// Package 142 (A151): a stereo encoder (interleaved frames) in Opus' general audio mode with the music signal type,
    /// instead of mono with VOIP and voice. It sends mono while <see cref="Mono"/> is set.
    /// </param>
    public VoiceEncoder(bool music = false)
    {
        Channels = music ? 2 : 1;
        if (native.Error is { } unusable) throw new InvalidOperationException(unusable);
        encoder = NativeObjectHandle.Take(native.EncoderCreate((uint)Channels, music ? (byte)1 : (byte)0), native.EncoderFree);
        Set(SetBitrate, ProtocolDefaults.Bitrate * 1000);
        Set(SetSignal, music ? SignalMusic : SignalVoice);
        Set(SetComplexity, Complexity);
        // Package 124: DTX stays off. Push-to-talk and voice activation already stop sending in pauses; within one, DTX
        // would send a comfort-noise frame about every 400 ms, which a receiver plays as a lone frame plus concealment,
        // a faint pulse. It also takes a steady tone for noise after a second and stops sending it.
        Set(SetDtx, 0);
        // In-band FEC: each packet also carries a coarse copy of the previous frame, so the receiver can rebuild a
        // single lost packet instead of guessing it. Opus only adds it when told to expect some loss.
        // Package 142: not for music. Opus carries FEC only in its speech coder (SILK), so with a loss hint (10 % from the
        // start) it leaves the music coder for it, and SILK's stereo blurs the sides: at 64 kbps a tone on one side came
        // out 2.6 dB below it on the other (measured with Concentus, see PLAN.md). Without FEC the music coder keeps them
        // more than 70 dB apart; a lost music frame comes from the copy (Package 136) or the decoder's concealment.
        Set(SetInbandFec, music ? 0 : 1);
        Set(SetPacketLoss, LossAdapter.InitialHint);
    }

    void Set(int request, int value) => native.EncoderSet(encoder.Pointer, request, value);

    int Get(int request) => native.EncoderGet(encoder.Pointer, request);

    /// <summary>Package 142: forgets what it encoded before (a switch between speech and music).</summary>
    public void ResetState() => Set(ResetStateRequest, 0); // the reset request reads no value

    /// <summary>1 for voice, 2 for music (whose frames are then interleaved stereo, also when sent in mono).</summary>
    public int Channels { get; }

    /// <summary>
    /// Package 142 (A151): music below 64 kbps or from a mono device goes out in mono. The same encoder downmixes
    /// (Opus' forced channel count), so a switch across 64 kbps keeps its state and passes smoothly. Set before an encode.
    /// </summary>
    public bool Mono
    {
        get => Get(GetForceChannels) == 1;
        set
        {
            int channels = value ? 1 : 2; // 2, not Opus' own choice: stereo is decided by the rule above alone
            if (Get(GetForceChannels) != channels) Set(SetForceChannels, channels);
        }
    }

    /// <summary>Package 132 (A141): the channel's bitrate in kbps; set on the thread that encodes.</summary>
    public int BitrateKbps
    {
        get => Get(GetBitrate) / 1000;
        set => Set(SetBitrate, value * 1000);
    }

    /// <summary>The bitrate Opus is set to, in bits per second (tests read it back).</summary>
    internal int Bitrate => Get(GetBitrate);

    /// <summary>The complexity Opus is set to (tests read it back).</summary>
    internal int ComplexitySet => Get(GetComplexity);

    /// <summary>Whether Opus uses DTX (tests read it back; it stays off).</summary>
    internal bool UseDtx => Get(GetDtx) != 0;

    /// <summary>Package 135 (A144): the loss Opus expects, and adds FEC for; set on the thread that encodes.</summary>
    public int PacketLossPercent
    {
        get => Get(GetPacketLoss);
        set => Set(SetPacketLoss, value);
    }

    /// <param name="frame">20 ms, interleaved when stereo.</param>
    /// <returns>A new array: it is sent or kept by the mixer (and only made while sending, never per idle frame).</returns>
    public byte[] Encode(ReadOnlySpan<float> frame)
    {
        int length;
        fixed (float* samples = frame)
        fixed (byte* data = buffer)
            length = native.EncoderEncode(encoder.Pointer, samples, (uint)frame.Length, data, (uint)buffer.Length);
        return length > 0 ? buffer[..length] : [];
    }
}
