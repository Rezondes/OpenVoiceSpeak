using OVS.Tests.TestSupport;

namespace OVS.Tests;

/// <summary>
/// Package 180: THIRD-PARTY-NOTICES.md names every component statically linked into <c>ovs_audio.dll</c> and none
/// that is gone. The list here follows <c>cargo tree -e normal --features aec</c> of native/ovs_audio and the C/C++
/// parts its build links; a crate added there is added here and in the notices.
/// </summary>
public sealed class NoticesTests
{
    static readonly string[] NativeComponents =
    [
        "## RNNoise", "## WebRTC Audio Processing", "## Abseil", "## windows-rs", "## libopus", "## nnnoiseless",
        "anymap3", "array-init", "audiopus_sys", "cfg-if", "easyfft", "generic_singleton", "lock_api", "parking_lot",
        "parking_lot_core", "num-complex", "num-integer", "num-traits", "once_cell", "primal-check", "realfft", "rustfft",
        "scopeguard", "serde", "serde_core", "smallvec", "strength_reduce", "transpose",
    ];

    static readonly string[] Gone = ["ovs_aec", "rnnoise.dll", "YellowDogMan", "MinGW", "Concentus"];

    static string Notices => File.ReadAllText(Path.Combine(RepoPaths.Root, "THIRD-PARTY-NOTICES.md"));

    [Fact]
    public void ThirdPartyNotices_NativeComponents_EachNamedAndNoneGone()
    {
        var notices = Notices;
        foreach (var component in NativeComponents) Assert.Contains(component, notices, StringComparison.Ordinal);
        foreach (var component in Gone) Assert.DoesNotContain(component, notices, StringComparison.Ordinal);
    }
}
