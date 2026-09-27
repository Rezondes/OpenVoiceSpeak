using System.Net;
using System.Security.Cryptography;
using OVS.Client.Net;
using OVS.Shared.Identity;
using OVS.Shared.Protocol;
using OVS.Shared.Voice;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client;

public sealed class ClientNetTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("ovs-client-").FullName;
    KnownServers Known() => new(Path.Combine(dir, "known_servers.json"));

    public void Dispose() => Directory.Delete(dir, true);

    Task<ClientConnection> Connect(TestServer server, string nick, ClientIdentity? id = null, Func<TofuPrompt, Task<bool>>? confirm = null,
        KnownServers? known = null, string? password = null) =>
        ClientConnection.ConnectAsync("127.0.0.1", server.Port, id ?? ClientIdentity.Create(), nick, password,
            known ?? Known(), confirm ?? (_ => Task.FromResult(true)));

    [Fact]
    public void KnownServers_Check_Cases()
    {
        var known = Known();
        Assert.Equal(TofuResult.Unknown, known.Check("Host", 7000, "aa"));
        known.Trust("Host", 7000, "aa");
        Assert.Equal(TofuResult.Known, Known().Check("host", 7000, "aa"));
        Assert.Equal(TofuResult.Mismatch, Known().Check("host", 7000, "bb"));
        Assert.Equal(TofuResult.Unknown, Known().Check("host", 7001, "aa"));
    }

    [Theory]
    [InlineData(TofuResult.Unknown, true)]
    [InlineData(TofuResult.Mismatch, false)] // a changed certificate must not be accepted by just pressing Enter
    public void TofuPrompt_DefaultButton(TofuResult result, bool acceptIsDefault) =>
        Assert.Equal(acceptIsDefault, new TofuPrompt("h", 1, "fp", result).AcceptIsDefault);

    [Fact]
    public void Identity_LoadOrCreate_Twice_SameFingerprint()
    {
        using var a = ClientStorage.LoadOrCreateIdentity(dir);
        using var b = ClientStorage.LoadOrCreateIdentity(dir);
        Assert.Equal(a.Fingerprint, b.Fingerprint);
    }

    [Fact]
    public async Task FirstConnect_AsksAndStores_SecondConnectSilent()
    {
        await using var server = await TestServer.StartAsync();
        var prompts = new List<TofuPrompt>();
        Task<bool> Confirm(TofuPrompt p)
        {
            prompts.Add(p);
            return Task.FromResult(true);
        }

        await (await Connect(server, "a", confirm: Confirm)).DisposeAsync();
        await (await Connect(server, "b", confirm: Confirm)).DisposeAsync();

        var prompt = Assert.Single(prompts);
        Assert.Equal(TofuResult.Unknown, prompt.Result);
        Assert.Equal(CertFingerprint.Of(server.Certificate), prompt.Fingerprint);
    }

    [Fact]
    public async Task ChangedCert_Mismatch_Refused_NoHelloSent()
    {
        await using var server = await TestServer.StartAsync();
        var known = Known();
        known.Trust("127.0.0.1", server.Port, Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)));
        TofuPrompt? seen = null;

        await Assert.ThrowsAsync<TofuRejectedException>(() => Connect(server, "a", known: known, confirm: p =>
        {
            seen = p;
            return Task.FromResult(false);
        }));
        Assert.Equal(TofuResult.Mismatch, seen!.Result);
        Assert.Equal(0, server.State.SessionCount);
        Assert.DoesNotContain(server.Log, l => l.Contains("verbunden"));
    }

    [Fact]
    public async Task Rejected_Throws_WithCode()
    {
        await using var server = await TestServer.StartAsync(password: "pw");
        var e = await Assert.ThrowsAsync<ConnectionRejectedException>(() => Connect(server, "a"));
        Assert.Equal(Codes.WrongPassword, e.Code);
    }

    [Fact]
    public async Task Connect_MirrorShowsSelfInLobby_AndSecondClient()
    {
        await using var server = await TestServer.StartAsync();
        await using var first = await Connect(server, "anna");
        var mirror = new StateMirror(first.Welcome);
        var joined = new TaskCompletionSource();
        first.MessageReceived += m =>
        {
            lock (mirror) mirror.Apply(m);
            if (m is UserJoined) joined.TrySetResult();
        };

        Assert.Equal(mirror.DefaultChannelId, mirror.Self!.ChannelId);
        await using var second = await Connect(server, "bert");
        await joined.Task.WaitAsync(TimeSpan.FromSeconds(3));
        lock (mirror) Assert.Contains(mirror.Users.Values, u => u.Nickname == "bert");
    }

    [Fact]
    public async Task ServerKick_RaisesDisconnectedWithReason()
    {
        var admin = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(TestServer.Grant(admin, "Admin"));
        await using var a = await TestClient.ConnectAsync(server, identity: admin);
        await using var victim = await Connect(server, "opfer");
        var reason = new TaskCompletionSource<(string, string?)>();
        victim.Disconnected += (r, d) => reason.TrySetResult((r, d));

        await a.SendAsync(new Kick(victim.Welcome.SessionId, "tschüss"));
        Assert.Equal((Codes.Kicked, "tschüss"), await reason.Task.WaitAsync(TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task Voice_SendFromA_ReceivedByB_Identical()
    {
        await using var server = await TestServer.StartAsync();
        await using var a = await Connect(server, "a");
        await using var b = await Connect(server, "b");
        using var va = new VoiceClient(server.VoiceEndPoint, a.Welcome.SessionId, Convert.FromBase64String(a.Welcome.VoiceKey));
        using var vb = new VoiceClient(server.VoiceEndPoint, b.Welcome.SessionId, Convert.FromBase64String(b.Welcome.VoiceKey));
        var received = new TaskCompletionSource<(uint Speaker, byte Target, byte[] Opus)>();
        vb.VoiceReceived += (speaker, _, target, opus) => received.TrySetResult((speaker, target, opus));
        va.Start();
        vb.Start();
        await WaitUntil(() => va.Reachable && vb.Reachable);

        var opus = RandomNumberGenerator.GetBytes(70);
        va.SendVoice(opus, VoiceHeader.TargetChannel);
        var got = await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(a.Welcome.SessionId, got.Speaker);
        Assert.Equal(VoiceHeader.TargetChannel, got.Target);
        Assert.Equal(opus, got.Opus);
    }

    [Fact]
    public async Task Voice_PingBetweenFrames_KeepsSpeakerSeqContiguous()
    {
        // Regression: pings used to consume the sequence the receiver's jitter buffer orders by,
        // so every keep-alive during speech looked like a lost frame (+20 ms latency each time).
        await using var server = await TestServer.StartAsync();
        await using var a = await Connect(server, "a");
        await using var b = await Connect(server, "b");
        using var va = new VoiceClient(server.VoiceEndPoint, a.Welcome.SessionId, Convert.FromBase64String(a.Welcome.VoiceKey));
        using var vb = new VoiceClient(server.VoiceEndPoint, b.Welcome.SessionId, Convert.FromBase64String(b.Welcome.VoiceKey));
        var seqs = new List<uint>();
        var two = new TaskCompletionSource();
        vb.VoiceReceived += (_, speakerSeq, _, _) =>
        {
            lock (seqs)
            {
                seqs.Add(speakerSeq);
                if (seqs.Count == 2) two.TrySetResult();
            }
        };
        va.Start();
        vb.Start();
        await WaitUntil(() => va.Reachable && vb.Reachable);

        va.SendVoice([1, 2, 3], VoiceHeader.TargetChannel);
        va.SendPing();
        va.SendVoice([4, 5, 6], VoiceHeader.TargetChannel);
        await two.Task.WaitAsync(TimeSpan.FromSeconds(3));

        lock (seqs) Assert.Equal(seqs[0] + 1, seqs[1]);
    }

    static async Task WaitUntil(Func<bool> condition)
    {
        for (int i = 0; i < 100 && !condition(); i++) await Task.Delay(20);
        Assert.True(condition());
    }
}
