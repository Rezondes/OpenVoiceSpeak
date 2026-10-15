using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;

namespace OVS.Client.Audio.Rust;

/// <summary>
/// Package 172 (A180): the native audio library <c>ovs_audio</c> (see native/ovs_audio), loaded by hand like
/// <see cref="Processing.EchoCanceller"/>: a library that is missing, of another ABI version or without an export
/// shows up as <see cref="Error"/>, never as an exception. Loaded once per library name for the life of the process;
/// it holds the exports as function pointers (Package 173: the speaker's).
/// </summary>
internal sealed unsafe class NativeAudio
{
    /// <summary>The native library's name.</summary>
    public const string Library = "ovs_audio";

    /// <summary>The ABI version this client speaks; a library reporting another one is not used.</summary>
    public const uint AbiVersion = 1;

    static readonly ConcurrentDictionary<string, Lazy<NativeAudio>> Loaded = new();

    NativeAudio(string? error, uint foundAbiVersion) => (Error, FoundAbiVersion) = (error, foundAbiVersion);

    /// <summary>Why the library cannot be used, or null when it can.</summary>
    public string? Error { get; }

    /// <summary>The ABI version the library reported; 0 when it did not load.</summary>
    public uint FoundAbiVersion { get; }

    /// <summary>ovs_audio_last_error: copies the thread's last error text (UTF-8) and returns its whole length.</summary>
    internal delegate* unmanaged[Cdecl]<byte*, nuint, nuint> LastErrorText { get; private set; }
    /// <summary>ovs_audio_speaker_open: opens and starts the speaker of an MMDevice id (null: the default); 0 with the HRESULT on failure.</summary>
    internal delegate* unmanaged[Cdecl]<char*, int*, nint> SpeakerOpen { get; private set; }
    /// <summary>ovs_audio_speaker_open_fake: a speaker without a device, for tests.</summary>
    internal delegate* unmanaged[Cdecl]<nint> SpeakerOpenFake { get; private set; }
    /// <summary>ovs_audio_speaker_close: stops the device and frees the speaker.</summary>
    internal delegate* unmanaged[Cdecl]<nint, void> SpeakerClose { get; private set; }
    /// <summary>ovs_audio_speaker_write: queues interleaved stereo frames; returns how many fitted.</summary>
    internal delegate* unmanaged[Cdecl]<nint, float*, uint, uint> SpeakerWrite { get; private set; }
    /// <summary>ovs_audio_speaker_buffered_frames: the frames waiting to be played.</summary>
    internal delegate* unmanaged[Cdecl]<nint, uint> SpeakerBufferedFrames { get; private set; }
    /// <summary>ovs_audio_speaker_error: the HRESULT that ended the device stream, 0 while it plays.</summary>
    internal delegate* unmanaged[Cdecl]<nint, int> SpeakerError { get; private set; }
    /// <summary>ovs_audio_speaker_underrun_frames: silent frames played inside the audio.</summary>
    internal delegate* unmanaged[Cdecl]<nint, ulong> SpeakerUnderrunFrames { get; private set; }
    /// <summary>ovs_audio_speaker_mmcss_task: the render thread's MMCSS task index, 0 when not registered.</summary>
    internal delegate* unmanaged[Cdecl]<nint, uint> SpeakerMmcssTask { get; private set; }
    /// <summary>ovs_audio_speaker_fake_render: renders the fake speaker by hand, as its device would.</summary>
    internal delegate* unmanaged[Cdecl]<nint, float*, uint, void> SpeakerFakeRender { get; private set; }
    /// <summary>ovs_audio_speaker_fake_fail: ends the fake speaker's stream with an HRESULT.</summary>
    internal delegate* unmanaged[Cdecl]<nint, int, void> SpeakerFakeFail { get; private set; }
    /// <summary>ovs_audio_sound_load: loads (or replaces) a sound's mono samples by id, process-wide (Package 174).</summary>
    internal delegate* unmanaged[Cdecl]<uint, float*, uint, void> SoundLoad { get; private set; }
    /// <summary>ovs_audio_speaker_play_sound: starts a loaded sound with the next frame written; 1 when it started.</summary>
    internal delegate* unmanaged[Cdecl]<nint, uint, float, ulong, byte, byte> SpeakerPlaySound { get; private set; }
    /// <summary>ovs_audio_speaker_sounds_playing: the sounds playing or waiting to start.</summary>
    internal delegate* unmanaged[Cdecl]<nint, uint> SpeakerSoundsPlaying { get; private set; }
    /// <summary>ovs_audio_jitter_create: a new, empty jitter buffer with its decoder (Package 175).</summary>
    internal delegate* unmanaged[Cdecl]<nint> JitterCreate { get; private set; }
    /// <summary>ovs_audio_jitter_free: frees a jitter buffer.</summary>
    internal delegate* unmanaged[Cdecl]<nint, void> JitterFree { get; private set; }
    /// <summary>ovs_audio_jitter_push: takes a frame and the copy of the one before it (null when none).</summary>
    internal delegate* unmanaged[Cdecl]<nint, uint, byte*, uint, byte, double, byte*, uint, void> JitterPush { get; private set; }
    /// <summary>ovs_audio_jitter_pull: the next 20 ms of interleaved stereo; 0 while buffering or idle.</summary>
    internal delegate* unmanaged[Cdecl]<nint, float*, byte> JitterPull { get; private set; }
    /// <summary>ovs_audio_jitter_stats: the buffer's counts and measures.</summary>
    internal delegate* unmanaged[Cdecl]<nint, NativeJitterStats*, void> JitterStats { get; private set; }
    /// <summary>ovs_audio_decoder_create: a new Opus decoder (48 kHz, stereo out).</summary>
    internal delegate* unmanaged[Cdecl]<nint> DecoderCreate { get; private set; }
    /// <summary>ovs_audio_decoder_free: frees a decoder.</summary>
    internal delegate* unmanaged[Cdecl]<nint, void> DecoderFree { get; private set; }
    /// <summary>ovs_audio_decoder_decode: decodes (or with FEC rebuilds the frame before) a packet into a stereo frame.</summary>
    internal delegate* unmanaged[Cdecl]<nint, byte*, uint, byte, float*, void> DecoderDecode { get; private set; }

    /// <summary>ovs_audio_mixer_create: a new voice mixer with its pool of speakers (Package 176).</summary>
    internal delegate* unmanaged[Cdecl]<nint> MixerCreate { get; private set; }
    /// <summary>ovs_audio_mixer_free: lets go of the client's reference to a mixer.</summary>
    internal delegate* unmanaged[Cdecl]<nint, void> MixerFree { get; private set; }
    /// <summary>ovs_audio_mixer_push: a received packet for the mixer, from any thread.</summary>
    internal delegate* unmanaged[Cdecl]<nint, uint, uint, byte*, uint, byte, double, byte*, uint, void> MixerPush { get; private set; }
    /// <summary>ovs_audio_mixer_mix: mixes one whole frame (no device, tests).</summary>
    internal delegate* unmanaged[Cdecl]<nint, float*, void> MixerMix { get; private set; }
    /// <summary>ovs_audio_mixer_snapshot: the mixer's last snapshot.</summary>
    internal delegate* unmanaged[Cdecl]<nint, NativeMixerSnapshot*, void> MixerSnapshot { get; private set; }
    /// <summary>ovs_audio_mixer_settle: takes the waiting packets and commands and refreshes the snapshot without mixing.</summary>
    internal delegate* unmanaged[Cdecl]<nint, void> MixerSettle { get; private set; }
    /// <summary>ovs_audio_mixer_set_gains: each listed speaker's final gain and everyone else's.</summary>
    internal delegate* unmanaged[Cdecl]<nint, uint*, float*, uint, float, void> MixerSetGains { get; private set; }
    /// <summary>ovs_audio_mixer_remove: drops one speaker's buffer.</summary>
    internal delegate* unmanaged[Cdecl]<nint, uint, void> MixerRemove { get; private set; }
    /// <summary>ovs_audio_mixer_clear: drops every speaker's buffer.</summary>
    internal delegate* unmanaged[Cdecl]<nint, void> MixerClear { get; private set; }
    /// <summary>ovs_audio_mixer_set_flags: deafened and the self test.</summary>
    internal delegate* unmanaged[Cdecl]<nint, byte, byte, void> MixerSetFlags { get; private set; }
    /// <summary>ovs_audio_mixer_read_far_end: takes mono far-end samples the speaker played.</summary>
    internal delegate* unmanaged[Cdecl]<nint, float*, uint, uint> MixerReadFarEnd { get; private set; }
    /// <summary>ovs_audio_limiter_create: a new soft limiter.</summary>
    internal delegate* unmanaged[Cdecl]<nint> LimiterCreate { get; private set; }
    /// <summary>ovs_audio_limiter_free: frees a limiter.</summary>
    internal delegate* unmanaged[Cdecl]<nint, void> LimiterFree { get; private set; }
    /// <summary>ovs_audio_limiter_process: limits a frame in place; returns the gain now.</summary>
    internal delegate* unmanaged[Cdecl]<nint, float*, uint, float> LimiterProcess { get; private set; }
    /// <summary>ovs_audio_speaker_attach_mixer: lets the speaker play the voices of a mixer; 1 when attached.</summary>
    internal delegate* unmanaged[Cdecl]<nint, nint, byte> SpeakerAttachMixer { get; private set; }

    /// <summary>The library of this name, loaded on the first call.</summary>
    public static NativeAudio Load(string library = Library) => Loaded.GetOrAdd(library, name => new Lazy<NativeAudio>(() => Open(name))).Value;

    static NativeAudio Open(string library)
    {
        try
        {
            var handle = NativeLibrary.Load(library, typeof(NativeAudio).Assembly, null);
            var found = ((delegate* unmanaged[Cdecl]<uint>)NativeLibrary.GetExport(handle, "ovs_audio_abi_version"))();
            if (CheckAbi(found) is { } mismatch) return new NativeAudio(mismatch, found);
            var native = new NativeAudio(null, found);
            native.BindSpeaker(handle);
            native.BindVoice(handle);
            native.BindMixer(handle);
            return native;
        }
        #pragma warning disable CA1031 // missing, wrong architecture, a missing export: the client runs without audio (A180)
        catch (Exception exception) // missing, wrong architecture, a missing export: the client runs without audio (A180)
        #pragma warning restore CA1031
        {
            return new NativeAudio(exception.Message, 0);
        }
    }

    /// <summary>The last error and the speaker's exports (Packages 172 to 174).</summary>
    void BindSpeaker(nint handle)
    {
        LastErrorText = (delegate* unmanaged[Cdecl]<byte*, nuint, nuint>)NativeLibrary.GetExport(handle, "ovs_audio_last_error");
        SpeakerOpen = (delegate* unmanaged[Cdecl]<char*, int*, nint>)NativeLibrary.GetExport(handle, "ovs_audio_speaker_open");
        SpeakerOpenFake = (delegate* unmanaged[Cdecl]<nint>)NativeLibrary.GetExport(handle, "ovs_audio_speaker_open_fake");
        SpeakerClose = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(handle, "ovs_audio_speaker_close");
        SpeakerWrite = (delegate* unmanaged[Cdecl]<nint, float*, uint, uint>)NativeLibrary.GetExport(handle, "ovs_audio_speaker_write");
        SpeakerBufferedFrames = (delegate* unmanaged[Cdecl]<nint, uint>)NativeLibrary.GetExport(handle, "ovs_audio_speaker_buffered_frames");
        SpeakerError = (delegate* unmanaged[Cdecl]<nint, int>)NativeLibrary.GetExport(handle, "ovs_audio_speaker_error");
        SpeakerUnderrunFrames = (delegate* unmanaged[Cdecl]<nint, ulong>)NativeLibrary.GetExport(handle, "ovs_audio_speaker_underrun_frames");
        SpeakerMmcssTask = (delegate* unmanaged[Cdecl]<nint, uint>)NativeLibrary.GetExport(handle, "ovs_audio_speaker_mmcss_task");
        SpeakerFakeRender = (delegate* unmanaged[Cdecl]<nint, float*, uint, void>)NativeLibrary.GetExport(handle, "ovs_audio_speaker_fake_render");
        SpeakerFakeFail = (delegate* unmanaged[Cdecl]<nint, int, void>)NativeLibrary.GetExport(handle, "ovs_audio_speaker_fake_fail");
        SpeakerPlaySound = (delegate* unmanaged[Cdecl]<nint, uint, float, ulong, byte, byte>)NativeLibrary.GetExport(handle, "ovs_audio_speaker_play_sound");
        SpeakerSoundsPlaying = (delegate* unmanaged[Cdecl]<nint, uint>)NativeLibrary.GetExport(handle, "ovs_audio_speaker_sounds_playing");
    }

    /// <summary>The sounds', jitter buffer's and decoder's exports (Packages 174, 175).</summary>
    void BindVoice(nint handle)
    {
        SoundLoad = (delegate* unmanaged[Cdecl]<uint, float*, uint, void>)NativeLibrary.GetExport(handle, "ovs_audio_sound_load");
        JitterCreate = (delegate* unmanaged[Cdecl]<nint>)NativeLibrary.GetExport(handle, "ovs_audio_jitter_create");
        JitterFree = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(handle, "ovs_audio_jitter_free");
        JitterPush = (delegate* unmanaged[Cdecl]<nint, uint, byte*, uint, byte, double, byte*, uint, void>)NativeLibrary.GetExport(handle, "ovs_audio_jitter_push");
        JitterPull = (delegate* unmanaged[Cdecl]<nint, float*, byte>)NativeLibrary.GetExport(handle, "ovs_audio_jitter_pull");
        JitterStats = (delegate* unmanaged[Cdecl]<nint, NativeJitterStats*, void>)NativeLibrary.GetExport(handle, "ovs_audio_jitter_stats");
        DecoderCreate = (delegate* unmanaged[Cdecl]<nint>)NativeLibrary.GetExport(handle, "ovs_audio_decoder_create");
        DecoderFree = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(handle, "ovs_audio_decoder_free");
        DecoderDecode = (delegate* unmanaged[Cdecl]<nint, byte*, uint, byte, float*, void>)NativeLibrary.GetExport(handle, "ovs_audio_decoder_decode");
    }

    /// <summary>The mixer's and limiter's exports (Package 176).</summary>
    void BindMixer(nint handle)
    {
        MixerCreate = (delegate* unmanaged[Cdecl]<nint>)NativeLibrary.GetExport(handle, "ovs_audio_mixer_create");
        MixerFree = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(handle, "ovs_audio_mixer_free");
        MixerPush = (delegate* unmanaged[Cdecl]<nint, uint, uint, byte*, uint, byte, double, byte*, uint, void>)NativeLibrary.GetExport(handle, "ovs_audio_mixer_push");
        MixerMix = (delegate* unmanaged[Cdecl]<nint, float*, void>)NativeLibrary.GetExport(handle, "ovs_audio_mixer_mix");
        MixerSnapshot = (delegate* unmanaged[Cdecl]<nint, NativeMixerSnapshot*, void>)NativeLibrary.GetExport(handle, "ovs_audio_mixer_snapshot");
        MixerSettle = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(handle, "ovs_audio_mixer_settle");
        MixerSetGains = (delegate* unmanaged[Cdecl]<nint, uint*, float*, uint, float, void>)NativeLibrary.GetExport(handle, "ovs_audio_mixer_set_gains");
        MixerRemove = (delegate* unmanaged[Cdecl]<nint, uint, void>)NativeLibrary.GetExport(handle, "ovs_audio_mixer_remove");
        MixerClear = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(handle, "ovs_audio_mixer_clear");
        MixerSetFlags = (delegate* unmanaged[Cdecl]<nint, byte, byte, void>)NativeLibrary.GetExport(handle, "ovs_audio_mixer_set_flags");
        MixerReadFarEnd = (delegate* unmanaged[Cdecl]<nint, float*, uint, uint>)NativeLibrary.GetExport(handle, "ovs_audio_mixer_read_far_end");
        LimiterCreate = (delegate* unmanaged[Cdecl]<nint>)NativeLibrary.GetExport(handle, "ovs_audio_limiter_create");
        LimiterFree = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(handle, "ovs_audio_limiter_free");
        LimiterProcess = (delegate* unmanaged[Cdecl]<nint, float*, uint, float>)NativeLibrary.GetExport(handle, "ovs_audio_limiter_process");
        SpeakerAttachMixer = (delegate* unmanaged[Cdecl]<nint, nint, byte>)NativeLibrary.GetExport(handle, "ovs_audio_speaker_attach_mixer");
    }

    /// <summary>Null when the library's ABI version is this client's, else the reason with both versions.</summary>
    internal static string? CheckAbi(uint found) =>
        found == AbiVersion ? null : $"{Library} has ABI version {found}, this client needs {AbiVersion}";

    /// <summary>The text of the last error of an export on this thread (read right after the failing call).</summary>
    internal string LastError()
    {
        Span<byte> text = stackalloc byte[512];
        nuint length;
        fixed (byte* buffer = text) length = LastErrorText(buffer, (nuint)text.Length);
        return Encoding.UTF8.GetString(text[..(int)Math.Min(length, (nuint)text.Length)]);
    }
}
