using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace OVS.Client.Audio.Rust;

/// <summary>
/// Package 172 (A180): the native audio library <c>ovs_audio</c> (see native/ovs_audio), loaded by hand like
/// <see cref="Processing.EchoCanceller"/>: a library that is missing or of another ABI version shows up as
/// <see cref="Error"/>, never as an exception. Loaded once per library name for the life of the process.
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

    /// <summary>The library of this name, loaded on the first call.</summary>
    public static NativeAudio Load(string library = Library) => Loaded.GetOrAdd(library, name => new Lazy<NativeAudio>(() => Open(name))).Value;

    static NativeAudio Open(string library)
    {
        try
        {
            var handle = NativeLibrary.Load(library, typeof(NativeAudio).Assembly, null);
            var abiVersion = (delegate* unmanaged[Cdecl]<uint>)NativeLibrary.GetExport(handle, "ovs_audio_abi_version");
            var found = abiVersion();
            return new NativeAudio(CheckAbi(found), found);
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
}
