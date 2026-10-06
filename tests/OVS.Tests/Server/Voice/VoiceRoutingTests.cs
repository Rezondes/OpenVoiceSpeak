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
    // Channels L, A, B, C with links A-B and B-C.
    static readonly Guid L = Guid.NewGuid(), A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid();

    static IReadOnlySet<Guid> Linked(Guid ch) =>
        ch == A ? new HashSet<Guid> { B } : ch == B ? new HashSet<Guid> { A, C } : ch == C ? new HashSet<Guid> { B } : new HashSet<Guid>();

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
        VoiceRouting.Recipients([sender, .. others], sender, target, Linked).Select(s => s.Id).Order().ToArray();

    /// <summary>Package 34: in a muted channel nobody is heard, not even an admin via link.</summary>
    [Theory]
    [InlineData(VoiceHeader.TargetChannel)]
    [InlineData(VoiceHeader.TargetLinked)]
    public void Recipients_MutedChannel_NobodyHearsSender(byte target)
    {
        var admin = S(1, A, Permission.All);
        var heard = VoiceRouting.Recipients([admin, S(2, A), S(3, B)], admin, target, Linked, ch => ch == A);
        Assert.Empty(heard);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Recipients_LinkIntoMutedChannel_Heard(bool tcp)
    {
        var sender = S(1, B, tcp: tcp);
        var heard = VoiceRouting.Recipients([sender, S(2, A, tcp: tcp), S(3, B, tcp: tcp)], sender, VoiceHeader.TargetLinked, Linked, ch => ch == A);
        Assert.Equal([2u, 3u], heard.Select(s => s.Id).Order());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Target0_LinkedChannel_OnlyOwnChannel(bool tcp)
    {
        var sender = S(1, A, tcp: tcp);
        Assert.Equal([2u], Route(sender, VoiceHeader.TargetChannel, S(2, A, tcp: tcp), S(3, B, tcp: tcp), S(4, L, tcp: tcp)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Target1_WithSpeakLinked_OwnPlusDirectLinks_NotTransitive(bool tcp)
    {
        var sender = S(1, A, tcp: tcp);
        Assert.Equal([2u, 3u], Route(sender, VoiceHeader.TargetLinked, S(2, A, tcp: tcp), S(3, B, tcp: !tcp), S(4, C, tcp: tcp), S(5, L, tcp: tcp)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Target1_WithoutSpeakLinked_OwnChannelOnly(bool tcp)
    {
        var sender = S(1, A, Permission.Speak, tcp: tcp);
        Assert.Equal([2u], Route(sender, VoiceHeader.TargetLinked, S(2, A, tcp: tcp), S(3, B, tcp: tcp)));
        Assert.Equal(VoiceHeader.TargetChannel, VoiceRouting.EffectiveTarget(sender, VoiceHeader.TargetLinked));
    }

    [Theory]
    [InlineData("noSpeak", false)]
    [InlineData("serverMuted", false)]
    [InlineData("selfMuted", false)]
    [InlineData("noSpeak", true)]
    [InlineData("serverMuted", true)]
    [InlineData("selfMuted", true)]
    public void SenderCannotSpeak_NoRecipients(string reason, bool tcp)
    {
        var sender = S(1, A, reason == "noSpeak" ? Permission.SpeakLinked : Talk, tcp: tcp);
        sender.ServerMuted = reason == "serverMuted";
        sender.SelfMuted = reason == "selfMuted";
        Assert.Empty(Route(sender, VoiceHeader.TargetLinked, S(2, A, tcp: tcp), S(3, B, tcp: tcp)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeafenedAndSender_Excluded(bool tcp)
    {
        var sender = S(1, A, tcp: tcp);
        var deaf = S(2, A, tcp: tcp);
        deaf.SelfDeafened = true;
        Assert.Equal([3u], Route(sender, VoiceHeader.TargetChannel, deaf, S(3, A, tcp: tcp)));
    }

    /// <summary>Package 141: a session neither bound on UDP nor on voice over TCP gets nothing.</summary>
    [Fact]
    public void UnboundEndpoint_Excluded()
    {
        Assert.Empty(Route(S(1, A), VoiceHeader.TargetChannel, S(2, A, bound: false)));
        Assert.Equal([3u], Route(S(1, A), VoiceHeader.TargetChannel, S(2, A, bound: false), S(3, A, tcp: true)));
    }

    [Fact]
    public void RateLimiter_Over60PerSecond_Dropped()
    {
        var time = new ManualTimeProvider();
        var limiter = new RateLimiter(time);
        int accepted = 0;
        for (int i = 0; i < 100; i++)
        {
            if (limiter.TryTake()) accepted++;
            time.Advance(TimeSpan.FromMilliseconds(10));
        }
        // 100 packets in 1 s: burst of 10 plus 60/s refill
        Assert.InRange(accepted, 60, 71);
    }

    [Fact]
    public void RateLimiter_BurstThenBlocked()
    {
        var limiter = new RateLimiter(new ManualTimeProvider());
        Assert.Equal(10, Enumerable.Range(0, 20).Count(_ => limiter.TryTake()));
    }
}
