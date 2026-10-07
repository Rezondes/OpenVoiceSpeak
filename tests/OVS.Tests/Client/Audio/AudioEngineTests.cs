using OVS.Client.Audio;

namespace OVS.Tests.Client.Audio;

/// <summary>Package 124: what went wrong with devices opened in one round reaches the user as one notice.</summary>
public class AudioEngineTests
{
    [Fact]
    public void JoinWarnings_RepeatedTexts_OneNoticeWithEachOnce() => Assert.Equal("a b", AudioEngine.JoinWarnings(["a", "b", "a"]));

    [Fact]
    public void JoinWarnings_NoWarnings_NoNotice() => Assert.Null(AudioEngine.JoinWarnings([]));
}
