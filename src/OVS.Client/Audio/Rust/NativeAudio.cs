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
    internal delegate* unmanaged[Cdecl]<byte*, nuint, nuint> LastErrorText { get; private init; }
    /// <summary>ovs_audio_speaker_open: opens and starts the speaker of an MMDevice id (null: the default); 0 with the HRESULT on failure.</summary>
    internal delegate* unmanaged[Cdecl]<char*, int*, nint> SpeakerOpen { get; private init; }
    /// <summary>ovs_audio_speaker_open_fake: a speaker without a device, for tests.</summary>
    internal delegate* unmanaged[Cdecl]<nint> SpeakerOpenFake { get; private init; }
    /// <summary>ovs_audio_speaker_close: stops the device and frees the speaker.</summary>
    internal delegate* unmanaged[Cdecl]<nint, void> SpeakerClose { get; private init; }
    /// <summary>ovs_audio_speaker_write: queues interleaved stereo frames; returns how many fitted.</summary>
    internal delegate* unmanaged[Cdecl]<nint, float*, uint, uint> SpeakerWrite { get; private init; }
    /// <summary>ovs_audio_speaker_buffered_frames: the frames waiting to be played.</summary>
    internal delegate* unmanaged[Cdecl]<nint, uint> SpeakerBufferedFrames { get; private init; }
    /// <summary>ovs_audio_speaker_error: the HRESULT that ended the device stream, 0 while it plays.</summary>
    internal delegate* unmanaged[Cdecl]<nint, int> SpeakerError { get; private init; }
    /// <summary>ovs_audio_speaker_underrun_frames: silent frames played inside the audio.</summary>
    internal delegate* unmanaged[Cdecl]<nint, ulong> SpeakerUnderrunFrames { get; private init; }
    /// <summary>ovs_audio_speaker_mmcss_task: the render thread's MMCSS task index, 0 when not registered.</summary>
    internal delegate* unmanaged[Cdecl]<nint, uint> SpeakerMmcssTask { get; private init; }
    /// <summary>ovs_audio_speaker_fake_render: renders the fake speaker by hand, as its device would.</summary>
    internal delegate* unmanaged[Cdecl]<nint, float*, uint, void> SpeakerFakeRender { get; private init; }
    /// <summary>ovs_audio_speaker_fake_fail: ends the fake speaker's stream with an HRESULT.</summary>
    internal delegate* unmanaged[Cdecl]<nint, int, void> SpeakerFakeFail { get; private init; }
    /// <summary>ovs_audio_sound_load: loads (or replaces) a sound's mono samples by id, process-wide (Package 174).</summary>
    internal delegate* unmanaged[Cdecl]<uint, float*, uint, void> SoundLoad { get; private init; }
    /// <summary>ovs_audio_speaker_play_sound: starts a loaded sound with the next frame written; 1 when it started.</summary>
    internal delegate* unmanaged[Cdecl]<nint, uint, float, ulong, byte, byte> SpeakerPlaySound { get; private init; }
    /// <summary>ovs_audio_speaker_sounds_playing: the sounds playing or waiting to start.</summary>
    internal delegate* unmanaged[Cdecl]<nint, uint> SpeakerSoundsPlaying { get; private init; }

    /// <summary>The library of this name, loaded on the first call.</summary>
    public static NativeAudio Load(string library = Library) => Loaded.GetOrAdd(library, name => new Lazy<NativeAudio>(() => Open(name))).Value;

    static NativeAudio Open(string library)
    {
        try
        {
            var handle = NativeLibrary.Load(library, typeof(NativeAudio).Assembly, null);
            var found = ((delegate* unmanaged[Cdecl]<uint>)NativeLibrary.GetExport(handle, "ovs_audio_abi_version"))();
            if (CheckAbi(found) is { } mismatch) return new NativeAudio(mismatch, found);
            return new NativeAudio(null, found)
            {
                LastErrorText = (delegate* unmanaged[Cdecl]<byte*, nuint, nuint>)NativeLibrary.GetExport(handle, "ovs_audio_last_error"),
                SpeakerOpen = (delegate* unmanaged[Cdecl]<char*, int*, nint>)NativeLibrary.GetExport(handle, "ovs_audio_speaker_open"),
                SpeakerOpenFake = (delegate* unmanaged[Cdecl]<nint>)NativeLibrary.GetExport(handle, "ovs_audio_speaker_open_fake"),
                SpeakerClose = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(handle, "ovs_audio_speaker_close"),
                SpeakerWrite = (delegate* unmanaged[Cdecl]<nint, float*, uint, uint>)NativeLibrary.GetExport(handle, "ovs_audio_speaker_write"),
                SpeakerBufferedFrames = (delegate* unmanaged[Cdecl]<nint, uint>)NativeLibrary.GetExport(handle, "ovs_audio_speaker_buffered_frames"),
                SpeakerError = (delegate* unmanaged[Cdecl]<nint, int>)NativeLibrary.GetExport(handle, "ovs_audio_speaker_error"),
                SpeakerUnderrunFrames = (delegate* unmanaged[Cdecl]<nint, ulong>)NativeLibrary.GetExport(handle, "ovs_audio_speaker_underrun_frames"),
                SpeakerMmcssTask = (delegate* unmanaged[Cdecl]<nint, uint>)NativeLibrary.GetExport(handle, "ovs_audio_speaker_mmcss_task"),
                SpeakerFakeRender = (delegate* unmanaged[Cdecl]<nint, float*, uint, void>)NativeLibrary.GetExport(handle, "ovs_audio_speaker_fake_render"),
                SpeakerFakeFail = (delegate* unmanaged[Cdecl]<nint, int, void>)NativeLibrary.GetExport(handle, "ovs_audio_speaker_fake_fail"),
                SoundLoad = (delegate* unmanaged[Cdecl]<uint, float*, uint, void>)NativeLibrary.GetExport(handle, "ovs_audio_sound_load"),
                SpeakerPlaySound = (delegate* unmanaged[Cdecl]<nint, uint, float, ulong, byte, byte>)NativeLibrary.GetExport(handle, "ovs_audio_speaker_play_sound"),
                SpeakerSoundsPlaying = (delegate* unmanaged[Cdecl]<nint, uint>)NativeLibrary.GetExport(handle, "ovs_audio_speaker_sounds_playing"),
            };
        }
        #pragma warning disable CA1031 // missing, wrong architecture, a missing export: the client runs without audio (A180)
        catch (Exception exception) // missing, wrong architecture, a missing export: the client runs without audio (A180)
        #pragma warning restore CA1031
        {
            return new NativeAudio(exception.Message, 0);
        }
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
