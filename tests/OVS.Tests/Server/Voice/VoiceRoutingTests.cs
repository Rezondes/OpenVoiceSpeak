using System.Net;
using OVS.Server;
using OVS.Server.Throttling;
using OVS.Server.Voice;
using OVS.Shared.Permissions;
using OVS.Shared.Voice;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server.Voice;

public class VoiceRoutingTests
{
    // Channels Lobby, ChannelA, ChannelB, ChannelC with links A-B and B-C.
    static readonly Guid Lobby = Guid.NewGuid(), ChannelA = Guid.NewGuid(), ChannelB = Guid.NewGuid(), ChannelC = Guid.NewGuid();

    static IReadOnlySet<Guid> Linked(Guid ch) =>
        ch == ChannelA ? new HashSet<Guid> { ChannelB } : ch == ChannelB ? new HashSet<Guid> { ChannelA, ChannelC } : ch == ChannelC ? new HashSet<Guid> { ChannelB } : new HashSet<Guid>();

    const Permission Talk = Permission.Speak | Permission.SpeakLinked;

    /// <param name="tcp">Package 141 (AC3): a listener on voice over TCP instead of a bound UDP endpoint; routing is the same.</param>
    static Session S(uint id, Guid channel, Permission perms = Talk, bool bound = true, bool tcp = false) =>
        new(id, "fp" + id, "n" + id, IPAddress.Loopback, new byte[32], new ManualTimeProvider())
        {
            ChannelId = channel,
            Permissions = perms,
            Voice =
            {
                UdpEndpoint = bound && !tcp ? new IPEndPoint(IPAddress.Loopback, 1000 + (int)id) : null,
                TcpVoice = bound && tcp,
            },
        };

    static uint[] Route(Session sender, byte target, params Session[] others) =>
        VoiceRouting.Recipients([sender, .. others], sender, target, Linked).Select(session => session.Id).Order().ToArray();

    /// <summary>Package 34: in a muted channel nobody is heard, not even an admin via link.</summary>
    [Theory]
    [InlineData(VoiceHeader.TargetChannel)]
    [InlineData(VoiceHeader.TargetLinked)]
    public void Recipients_MutedChannel_NobodyHearsSender(byte target)
    {
        var admin = S(1, ChannelA, Permission.All);
        var heard = VoiceRouting.Recipients([admin, S(2, ChannelA), S(3, ChannelB)], admin, target, Linked, ch => ch == ChannelA);
        Assert.Empty(heard);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Recipients_LinkIntoMutedChannel_Heard(bool tcp)
    {
        var sender = S(1, ChannelB, tcp: tcp);
        var heard = VoiceRouting.Recipients([sender, S(2, ChannelA, tcp: tcp), S(3, ChannelB, tcp: tcp)], sender, VoiceHeader.TargetLinked, Linked, ch => ch == ChannelA);
        Assert.Equal([2u, 3u], heard.Select(session => session.Id).Order());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Target0_LinkedChannel_OnlyOwnChannel(bool tcp)
    {
        var sender = S(1, ChannelA, tcp: tcp);
        Assert.Equal([2u], Route(sender, VoiceHeader.TargetChannel, S(2, ChannelA, tcp: tcp), S(3, ChannelB, tcp: tcp), S(4, Lobby, tcp: tcp)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Route_LinkTargetWithSpeakLinked_OwnAndDirectLinksNotTransitive(bool tcp)
    {
        var sender = S(1, ChannelA, tcp: tcp);
        Assert.Equal([2u, 3u], Route(sender, VoiceHeader.TargetLinked, S(2, ChannelA, tcp: tcp), S(3, ChannelB, tcp: !tcp), S(4, ChannelC, tcp: tcp), S(5, Lobby, tcp: tcp)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Target1_WithoutSpeakLinked_OwnChannelOnly(bool tcp)
    {
        var sender = S(1, ChannelA, Permission.Speak, tcp: tcp);
        Assert.Equal([2u], Route(sender, VoiceHeader.TargetLinked, S(2, ChannelA, tcp: tcp), S(3, ChannelB, tcp: tcp)));
        Assert.Equal(VoiceHeader.TargetChannel, VoiceRouting.EffectiveTarget(sender, VoiceHeader.TargetLinked));
    }

    [Theory]
    [InlineData("noSpeak", false)]
    [InlineData("serverMuted", false)]
    [InlineData("selfMuted", false)]
    [InlineData("noSpeak", true)]
    [InlineData("serverMuted", true)]
    [InlineData("selfMuted", true)]
    public void Route_SenderCannotSpeak_NoRecipients(string reason, bool tcp)
    {
        var sender = S(1, ChannelA, reason == "noSpeak" ? Permission.SpeakLinked : Talk, tcp: tcp);
        sender.ServerMuted = reason == "serverMuted";
        sender.SelfMuted = reason == "selfMuted";
        Assert.Empty(Route(sender, VoiceHeader.TargetLinked, S(2, ChannelA, tcp: tcp), S(3, ChannelB, tcp: tcp)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Route_DeafenedAndSender_Excluded(bool tcp)
    {
        var sender = S(1, ChannelA, tcp: tcp);
        var deaf = S(2, ChannelA, tcp: tcp);
        deaf.SelfDeafened = true;
        Assert.Equal([3u], Route(sender, VoiceHeader.TargetChannel, deaf, S(3, ChannelA, tcp: tcp)));
    }

    /// <summary>Package 141: a session neither bound on UDP nor on voice over TCP gets nothing.</summary>
    [Fact]
    public void Route_UnboundEndpoint_Excluded()
    {
        Assert.Empty(Route(S(1, ChannelA), VoiceHeader.TargetChannel, S(2, ChannelA, bound: false)));
        Assert.Equal([3u], Route(S(1, ChannelA), VoiceHeader.TargetChannel, S(2, ChannelA, bound: false), S(3, ChannelA, tcp: true)));
    }

    [Fact]
    public void RateLimiter_Over60PerSecond_Dropped()
    {
        var time = new ManualTimeProvider();
        var limiter = new RateLimiter(time);
        int accepted = 0;
        for (int index = 0; index < 100; index++)
        {
            if (limiter.TryTake()) accepted++;
            time.Advance(TimeSpan.FromMilliseconds(10));
        }
        // 100 packets in 1 s: burst of 10 plus 60/s refill
        Assert.InRange(accepted, 60, 71);
    }

    [Fact]
    public void RateLimiter_Burst_ThenBlocked()
    {
        var limiter = new RateLimiter(new ManualTimeProvider());
        Assert.Equal(10, Enumerable.Range(0, 20).Count(_ => limiter.TryTake()));
    }
}
