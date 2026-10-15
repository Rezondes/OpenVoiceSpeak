using OVS.Client.Audio.Rust;

namespace OVS.Tests.Client.Audio.Rust;

/// <summary>Package 172 (A180): the native audio library loads by hand, and a missing or foreign one is an error, not an exception.</summary>
public sealed class NativeAudioTests
{
    [Fact]
    public void Load_LibraryPresent_ReadsAbiVersionOne()
    {
        var native = NativeAudio.Load();

        Assert.Null(native.Error);
        Assert.Equal(1u, native.FoundAbiVersion);
    }

    [Fact]
    public void Load_LibraryMissing_ErrorNotThrown()
    {
        var native = NativeAudio.Load("ovs-no-such-audio");

        Assert.NotNull(native.Error);
        Assert.Equal(0u, native.FoundAbiVersion);
    }

    [Fact]
    public void Load_OtherAbiVersion_ErrorNamesBothVersions()
    {
        var error = NativeAudio.CheckAbi(2);

        Assert.Equal("ovs_audio has ABI version 2, this client needs 1", error);
        Assert.Null(NativeAudio.CheckAbi(NativeAudio.AbiVersion));
    }

    [Fact]
    public void Load_Twice_LoadedOnce()
    {
        Assert.Same(NativeAudio.Load(), NativeAudio.Load());
    }
}
