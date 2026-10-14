using OVS.Client.Converters;

namespace OVS.Tests.Client.Converters;

/// <summary>Package 160: streamer mode hides IP addresses and server addresses and shortens fingerprints.</summary>
public sealed class PrivacyConvertersTests
{
    static readonly string Fingerprint = new('a', 64);

    [Fact]
    public void Values_StreamerModeOnAndOff_HiddenAndShown()
    {
        Assert.Equal("•••", PrivacyConverters.Ip("203.0.113.7", true));
        Assert.Equal("203.0.113.7", PrivacyConverters.Ip("203.0.113.7", false));
        Assert.Equal("•••", PrivacyConverters.Address("voice.example.org", true));
        Assert.Equal("voice.example.org", PrivacyConverters.Address("voice.example.org", false));
        Assert.Equal(Fingerprint[..16], PrivacyConverters.Fingerprint(Fingerprint, true));
        Assert.Equal(Fingerprint, PrivacyConverters.Fingerprint(Fingerprint, false));
        Assert.Equal("abc", PrivacyConverters.Fingerprint("abc", true)); // already short
        // nothing to hide stays nothing: an empty field does not turn into dots
        Assert.Null(PrivacyConverters.Ip(null, true));
        Assert.Equal("", PrivacyConverters.Address("", true));
        Assert.Null(PrivacyConverters.Fingerprint(null, true));
        Assert.Null(PrivacyConverters.Text(null, true));

        // a server log line: every address hidden, the fingerprint short, the time and the rest kept
        var line = $"2026-09-30 12:00:05.000 anna connected ({Fingerprint}, 2001:db8:85a3::8a2e:370:7334) from 192.168.1.20, ::ffff:10.0.0.1, ::1 and 2001:0db8:0000:0000:0000:ff00:0042:8329";
        Assert.Equal($"2026-09-30 12:00:05.000 anna connected ({Fingerprint[..16]}, •••) from •••, •••, ••• and •••", PrivacyConverters.Text(line, true));
        Assert.Equal(line, PrivacyConverters.Text(line, false));
        Assert.Equal("Version 1.2.3 at 12:00:05", PrivacyConverters.Text("Version 1.2.3 at 12:00:05", true));
    }
}
