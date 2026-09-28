using System.Net;
using OVS.Server;
using OVS.Server.Voice;
using OVS.Shared.Permissions;
using OVS.Shared.Voice;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server;

public class VoiceRoutingTests
{
    // Channels L, A, B, C with links A-B and B-C.
    static readonly Guid L = Guid.NewGuid(), A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid();

    static IReadOnlySet<Guid> Linked(Guid ch) =>
        ch == A ? new HashSet<Guid> { B } : ch == B ? new HashSet<Guid> { A, C } : ch == C ? new HashSet<Guid> { B } : new HashSet<Guid>();

    const Permission Talk = Permission.Speak | Permission.SpeakLinked;

    static Session S(uint id, Guid channel, Permission perms = Talk, bool bound = true) =>
        new(id, "fp" + id, "n" + id, IPAddress.Loopback, new byte[32], new ManualTimeProvider())
        {
            ChannelId = channel,
            Permissions = perms,
            UdpEndpoint = bound ? new IPEndPoint(IPAddress.Loopback, 1000 + (int)id) : null,
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

    [Fact]
    public void Recipients_LinkIntoMutedChannel_Heard()
    {
        var sender = S(1, B);
        var heard = VoiceRouting.Recipients([sender, S(2, A), S(3, B)], sender, VoiceHeader.TargetLinked, Linked, ch => ch == A);
        Assert.Equal([2u, 3u], heard.Select(s => s.Id).Order());
    }

    [Fact]
    public void Target0_LinkedChannel_OnlyOwnChannel()
    {
        var sender = S(1, A);
        Assert.Equal([2u], Route(sender, VoiceHeader.TargetChannel, S(2, A), S(3, B), S(4, L)));
    }

    [Fact]
    public void Target1_WithSpeakLinked_OwnPlusDirectLinks_NotTransitive()
    {
        var sender = S(1, A);
        Assert.Equal([2u, 3u], Route(sender, VoiceHeader.TargetLinked, S(2, A), S(3, B), S(4, C), S(5, L)));
    }

    [Fact]
    public void Target1_WithoutSpeakLinked_OwnChannelOnly()
    {
        var sender = S(1, A, Permission.Speak);
        Assert.Equal([2u], Route(sender, VoiceHeader.TargetLinked, S(2, A), S(3, B)));
        Assert.Equal(VoiceHeader.TargetChannel, VoiceRouting.EffectiveTarget(sender, VoiceHeader.TargetLinked));
    }

    [Theory]
    [InlineData("noSpeak")]
    [InlineData("serverMuted")]
    [InlineData("selfMuted")]
    public void SenderCannotSpeak_NoRecipients(string reason)
    {
        var sender = S(1, A, reason == "noSpeak" ? Permission.SpeakLinked : Talk);
        sender.ServerMuted = reason == "serverMuted";
        sender.SelfMuted = reason == "selfMuted";
        Assert.Empty(Route(sender, VoiceHeader.TargetLinked, S(2, A), S(3, B)));
    }

    [Fact]
    public void DeafenedAndSender_Excluded()
    {
        var sender = S(1, A);
        var deaf = S(2, A);
        deaf.SelfDeafened = true;
        Assert.Equal([3u], Route(sender, VoiceHeader.TargetChannel, deaf, S(3, A)));
    }

    [Fact]
    public void UnboundEndpoint_Excluded()
    {
        Assert.Empty(Route(S(1, A), VoiceHeader.TargetChannel, S(2, A, bound: false)));
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
