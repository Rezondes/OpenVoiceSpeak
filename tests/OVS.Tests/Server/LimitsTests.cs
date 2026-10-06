using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using OVS.Server;
using OVS.Server.Data;
using OVS.Server.Data.Records;
using OVS.Shared.Identity;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.Groups;
using OVS.Shared.Protocol.Messages.Moderation;
using OVS.Shared.Protocol.Messages.ServerBackups;
using OVS.Shared.Protocol.Messages.ServerInfo;
using OVS.Shared.Protocol.Messages.Users;
using OVS.Shared.Protocol.Messages.VoiceRelay;
using OVS.Shared.Voice;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server;

/// <summary>Package 86: request budget, costly requests, outbox limits and the accept path.</summary>
public sealed class LimitsTests
{
    /// <summary>Just enough PNG for the server's header check, filled with random bytes up to size.</summary>
    static byte[] Png(int size)
    {
        var png = new byte[size];
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(png, 0);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(8), 13);
        "IHDR"u8.CopyTo(png.AsSpan(12));
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16), 256);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20), 256);
        Random.Shared.NextBytes(png.AsSpan(33));
        return png;
    }

    static string NewDataDirWithIcon()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ovs-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, ServerIconStore.FileName), Png(ServerIconFormat.MaxBytes));
        return dir;
    }

    [Fact]
    public async Task RequestFlood_RateLimited_ThenDisconnected()
    {
        var time = new ManualTimeProvider();
        await using var server = await TestServer.StartAsync(time: time);
        await using var client = await TestClient.ConnectAsync(server, "flut");
        var here = client.Welcome.Snapshot.DefaultChannelId;
        int n = 0;

        // The burst passes (joining the own channel is a no-op), the rest is refused.
        for (int i = 0; i < Limits.RequestBurst + 5; i++) await client.SendAsync(new JoinChannel(here) { RequestId = $"j{n++}" });
        var first = await client.WaitForAsync<Error>();
        Assert.Equal(($"j{(int)Limits.RequestBurst}", Codes.RateLimited), (first.RequestId, first.Code));

        // Keeps flooding: every half second more than the budget refills, so every batch ends refused.
        bool closed = false;
        for (int step = 0; step < 30 && !closed; step++)
        {
            time.Advance(TimeSpan.FromMilliseconds(500));
            string last = "";
            try
            {
                for (int i = 0; i < Limits.RequestsPerSecond; i++)
                    await client.SendAsync(new JoinChannel(here) { RequestId = last = $"j{n++}" });
                var error = await client.WaitForAsync<Error>(e => e.RequestId == last);
                Assert.Equal(Codes.RateLimited, error.Code);
            }
            catch (Exception e) when (e is EndOfStreamException or IOException)
            {
                closed = true;
            }
            if (step < 19) Assert.False(closed, $"disconnected too early, step {step}");
        }
        Assert.True(closed || await client.WaitClosedAsync());
        // The goodbye can reach the client a moment before the server writes its log line.
        for (int i = 0; i < 200 && !server.Log.Any(l => l.Contains("flut getrennt (zu viele Anfragen)")); i++) await Task.Delay(50);
        Assert.Contains(server.Log, l => l.Contains("flut getrennt (zu viele Anfragen)"));
    }

    /// <summary>
    /// Package 141 (AC4, A150): voice over TCP has its own budget (60 per second, burst 10, like UDP); more is dropped
    /// without an error or a disconnect, and it takes nothing from the request budget.
    /// </summary>
    [Fact]
    public async Task VoiceUp_OwnBudget()
    {
        var time = new ManualTimeProvider(); // nothing refills unless the test moves the clock
        await using var server = await TestServer.StartAsync(time: time);
        await using var talker = await TestClient.ConnectAsync(server, "viel");
        await using var listener = await TestClient.ConnectAsync(server, "ohr");
        await listener.SendAsync(new VoiceUp(0, 0, ""));
        for (int i = 0; i < 500 && !server.State.FindSession(listener.Id)!.TcpVoice; i++) await Task.Delay(20);

        for (uint n = 0; n < 30; n++) await talker.SendAsync(new VoiceUp(n, 0, Convert.ToBase64String([1, 2, 3])));
        for (uint n = 0; n < Limits.VoiceBurst; n++) Assert.Equal(n, (await listener.WaitForAsync<VoiceDown>()).Seq);
        await listener.AssertNoMessageAsync<VoiceDown>();

        var here = talker.Welcome.Snapshot.DefaultChannelId;
        for (int i = 0; i < Limits.RequestBurst; i++) await talker.SendAsync(new JoinChannel(here) { RequestId = $"j{i}" });
        await talker.SendAsync(new JoinChannel(here) { RequestId = "over" });
        var refused = await talker.WaitForAsync<Error>(); // the whole request burst passed: the voice took none of it
        Assert.Equal(("over", Codes.RateLimited), (refused.RequestId, refused.Code));

        time.Advance(TimeSpan.FromSeconds(1));
        await talker.SendAsync(new VoiceUp(99, 0, Convert.ToBase64String([4, 5, 6])));
        Assert.Equal(99u, (await listener.WaitForAsync<VoiceDown>()).Seq);
    }

    /// <summary>
    /// Package 141 (review): invalid VoiceUp count like refused requests (Package 86): invalid ones without a pause for
    /// FloodDisconnectAfter end the session. Over-budget valid ones are only dropped: TCP delivers in bursts after a
    /// retransmission stall, so an honest talker sending 20 frames at once every second for 12 s stays, as does one at 50
    /// frames per second.
    /// </summary>
    [Fact]
    public async Task VoiceUpFlood_Disconnected_TalkingIsNot()
    {
        var time = new ManualTimeProvider();
        await using var server = await TestServer.StartAsync(time: time);
        await using var talker = await TestClient.ConnectAsync(server, "redner");
        await using var flood = await TestClient.ConnectAsync(server, "flut");
        async Task Sync(TestClient c) // everything sent before is handled at the current time
        {
            await c.SendAsync(new Ping());
            await c.WaitForAsync<Pong>();
        }

        for (uint n = 0; n < 50 * 12; n++) // 12 s at 50 frames per second, below the budget of 60
        {
            await talker.SendAsync(new VoiceUp(n, 0, Convert.ToBase64String([1, 2, 3])));
            time.Advance(TimeSpan.FromMilliseconds(20));
            if (n % 25 == 0) await Sync(talker);
        }
        await Sync(talker);

        for (uint second = 0; second < 12; second++) // the same second of speech arriving as one burst after each stall
        {
            for (uint i = 0; i < 20; i++) await talker.SendAsync(new VoiceUp(1000 + second * 50 + i, 0, Convert.ToBase64String([1, 2, 3])));
            await Sync(talker);
            time.Advance(TimeSpan.FromMilliseconds(500));
            await Sync(talker);
            time.Advance(TimeSpan.FromMilliseconds(500));
        }
        await Sync(talker);

        var padded = Convert.ToBase64String(new byte[VoiceHeader.MaxOpusSize + 1]);
        bool closed = false;
        for (int step = 0; step < 30 && !closed; step++)
        {
            try
            {
                for (int i = 0; i < 5; i++) await flood.SendAsync(new VoiceUp((uint)i, 0, padded));
                await Sync(flood);
            }
            catch (Exception e) when (e is EndOfStreamException or IOException)
            {
                closed = true;
            }
            if (step < 19) Assert.False(closed, $"disconnected too early, step {step}");
            time.Advance(TimeSpan.FromMilliseconds(500));
        }
        Assert.True(closed || await flood.WaitClosedAsync());
        await Sync(talker); // still connected
    }

    /// <summary>Package 141: a listener that does not read loses voice once a second of it waits, never its connection.</summary>
    [Fact]
    public void VoiceDown_SlowListener_DroppedNotAborted()
    {
        using var session = new Session(1, "fp", "n", IPAddress.Loopback, new byte[32], new ManualTimeProvider());
        var frame = SessionOutbox.Encode(new VoiceDown(2, 0, 0, Convert.ToBase64String(new byte[1275])));
        for (int i = 0; i < 1000; i++) session.Outbox.SendVoice(frame);
        Assert.Null(session.Outbox.AbortReason);
        Assert.Equal(Limits.MaxQueuedVoice, session.Outbox.Outgoing.Count);
        session.Outbox.Send(new Pong()); // other messages still fit
        Assert.Equal(Limits.MaxQueuedVoice + 1, session.Outbox.Outgoing.Count);
    }

    [Fact]
    public async Task ServerIcon_CachedAndThrottled()
    {
        var dir = NewDataDirWithIcon();
        var store = new ServerIconStore(dir);
        Assert.Same(store.Base64, store.Base64); // built once, not per request

        var time = new ManualTimeProvider();
        await using var server = await TestServer.StartAsync(dataDir: dir, time: time);
        await using var client = await TestClient.ConnectAsync(server);
        await client.SendAsync(new GetServerIcon { RequestId = "i1" });
        Assert.Equal(store.Base64, (await client.WaitForAsync<ServerIcon>()).PngBase64);

        await client.SendAsync(new GetServerIcon { RequestId = "i2" });
        Assert.Equal(Codes.RateLimited, (await client.ErrorAsync("i2")).Code);

        time.Advance(Limits.IconInterval);
        await client.SendAsync(new GetServerIcon { RequestId = "i3" });
        Assert.Equal("i3", (await client.WaitForAsync<ServerIcon>()).RequestId);
    }

    [Fact]
    public async Task ServerIcon_ChangedLogo_AnsweredWithinInterval()
    {
        var admin = ClientIdentity.Create();
        var time = new ManualTimeProvider();
        await using var server = await TestServer.StartAsync(TestServer.Grant(admin, "Admin"), dataDir: NewDataDirWithIcon(), time: time);
        await using var client = await TestClient.ConnectAsync(server, "chef", admin);
        await client.SendAsync(new GetServerIcon { RequestId = "i1" });
        var first = await client.WaitForAsync<ServerIcon>(i => i.RequestId == "i1");

        // Changed right after the fetch: the first request for the new logo is answered at once, a repeat is not.
        await client.SendAsync(new SetServerIcon(Convert.ToBase64String(Png(1024))));
        var changed = await client.WaitForAsync<ServerSettingsChanged>(c => c.Settings.IconHash != first.Hash);
        await client.SendAsync(new GetServerIcon { RequestId = "i2" });
        Assert.Equal(changed.Settings.IconHash, (await client.WaitForAsync<ServerIcon>(i => i.RequestId == "i2")).Hash);
        await client.SendAsync(new GetServerIcon { RequestId = "i3" });
        Assert.Equal(Codes.RateLimited, (await client.ErrorAsync("i3")).Code);

        // Changed twice within the interval: still answered.
        await client.SendAsync(new SetServerIcon(Convert.ToBase64String(Png(2048))));
        var again = await client.WaitForAsync<ServerSettingsChanged>(c => c.Settings.IconHash != changed.Settings.IconHash);
        await client.SendAsync(new GetServerIcon { RequestId = "i4" });
        Assert.Equal(again.Settings.IconHash, (await client.WaitForAsync<ServerIcon>(i => i.RequestId == "i4")).Hash);
    }

    [Fact]
    public async Task CostlyRequests_OwnLimits()
    {
        var admin = ClientIdentity.Create();
        var time = new ManualTimeProvider();
        await using var server = await TestServer.StartAsync(TestServer.Grant(admin, "Admin"), time: time);
        await using var client = await TestClient.ConnectAsync(server, "chef", admin);

        for (int i = 0; i < Limits.ListBurst; i++)
        {
            await client.SendAsync(new ListUsers { RequestId = $"u{i}" });
            await client.WaitForAsync<UserList>(l => l.RequestId == $"u{i}");
        }
        await client.SendAsync(new ListUsers { RequestId = "zu viel" });
        Assert.Equal(Codes.RateLimited, (await client.ErrorAsync("zu viel")).Code);
        await client.SendAsync(new ListBans { RequestId = "andere liste" }); // every list has its own budget
        await client.WaitForAsync<BanList>(l => l.RequestId == "andere liste");

        await client.SendAsync(new CreateBackup { RequestId = "b1" });
        await client.WaitForAsync<BackupList>(l => l.RequestId == "b1");
        await client.SendAsync(new CreateBackup { RequestId = "b2" });
        Assert.Equal(Codes.RateLimited, (await client.ErrorAsync("b2")).Code);
        time.Advance(Limits.HeavyInterval);
        await client.SendAsync(new CreateBackup { RequestId = "b3" });
        await client.WaitForAsync<BackupList>(l => l.RequestId == "b3");
    }

    [Fact]
    public async Task ListPages_FreeOnlyAsFollowUpOfAFirstPage()
    {
        var admin = ClientIdentity.Create();
        var time = new ManualTimeProvider();
        await using var server = await TestServer.StartAsync(d =>
        {
            TestServer.Grant(admin, "Admin")(d);
            for (int i = 0; i < 449; i++) d.Users.Add(new UserRecord { Fingerprint = $"fp{i}", GroupIds = [WellKnownGroups.Guest], FirstSeen = time.GetUtcNow() });
        }, time: time);
        await using var client = await TestClient.ConnectAsync(server, "chef", admin);
        int n = 0;
        async Task<string> Ask(int offset)
        {
            var id = $"l{n++}";
            await client.SendAsync(new ListUsers(offset) { RequestId = id });
            while (true)
                switch (await client.NextAsync())
                {
                    case UserList l when l.RequestId == id: return "ok";
                    case Error e when e.RequestId == id: return e.Code;
                }
        }

        // Normal paging through all 450 users, a whole burst of times: never throttled.
        for (int round = 0; round < Limits.ListBurst; round++)
            foreach (var offset in new[] { 0, Limits.ListPageSize, 2 * Limits.ListPageSize })
                Assert.Equal("ok", await Ask(offset));

        // A page served in this round already counts like a first page.
        Assert.Equal(Codes.RateLimited, await Ask(Limits.ListPageSize));

        // Without a first page in the last 30 s every later page counts.
        time.Advance(TimeSpan.FromSeconds(31)); // the budget is full again
        for (int i = 0; i < Limits.ListBurst; i++) Assert.Equal("ok", await Ask(1));
        Assert.Equal(Codes.RateLimited, await Ask(1));

        // Asking for the same later page again within a round counts from the second time on.
        time.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal("ok", await Ask(0));
        for (int i = 0; i < Limits.ListBurst; i++) Assert.Equal("ok", await Ask(Limits.ListPageSize));
        Assert.Equal(Codes.RateLimited, await Ask(Limits.ListPageSize));
    }

    [Fact]
    public async Task AdminTokenGuessing_Throttled_Logged()
    {
        var time = new ManualTimeProvider();
        await using var server = await TestServer.StartAsync(time: time);
        await using var a = await TestClient.ConnectAsync(server, "rater");
        await using var b = await TestClient.ConnectAsync(server, "zweiter");
        var token = server.State.PendingAdminToken!;

        for (int i = 0; i < Limits.AdminTokenFailures; i++)
        {
            await a.SendAsync(new RedeemAdminToken("falsch") { RequestId = $"t{i}" });
            Assert.Equal(Codes.InvalidToken, (await a.ErrorAsync($"t{i}")).Code);
        }
        Assert.Equal(Limits.AdminTokenFailures, server.Log.Count(l => l.Contains("Admin-Token falsch von rater")));

        // Same IP, other session: blocked too, even with the right token.
        await b.SendAsync(new RedeemAdminToken(token) { RequestId = "richtig" });
        Assert.Equal(Codes.RateLimited, (await b.ErrorAsync("richtig")).Code);
        Assert.Equal(token, server.State.PendingAdminToken);

        time.Advance(Limits.AdminTokenWindow);
        await b.SendAsync(new RedeemAdminToken(token) { RequestId = "spaeter" });
        await b.WaitForAsync<UserUpdated>(u => u.User.SessionId == b.Id);
        Assert.Null(server.State.PendingAdminToken);
    }

    [Fact]
    public async Task NoOpJoinAndSelfState_NoBroadcast()
    {
        await using var server = await TestServer.StartAsync();
        await using var a = await TestClient.ConnectAsync(server, "ruhig");
        await using var b = await TestClient.ConnectAsync(server);
        await a.WaitForAsync<UserJoined>(); // b's arrival
        int logLines = server.Log.Count;

        await a.SendAsync(new JoinChannel(a.Welcome.Snapshot.DefaultChannelId));
        await a.SendAsync(new SetSelfState(false, false));
        await a.SendAsync(new SetSelfState(true, false)); // a real change, as a marker

        var first = await b.WaitForAsync<UserUpdated>();
        Assert.True(first.User.SelfMuted);
        Assert.DoesNotContain(server.Log.Skip(logLines), l => l.Contains("ruhig"));
    }

    [Fact]
    public async Task LongRequestId_Rejected()
    {
        await using var server = await TestServer.StartAsync();
        await using var a = await TestClient.ConnectAsync(server);
        await using var b = await TestClient.ConnectAsync(server);
        await a.WaitForAsync<UserJoined>();
        var id = new string('x', 1000);

        await a.SendAsync(new SetSelfState(true, false) { RequestId = id });
        var error = await a.WaitForAsync<Error>();
        Assert.Equal((id[..Limits.MaxRequestIdLength], Codes.InvalidValue), (error.RequestId, error.Code));

        await a.SendAsync(new SetSelfState(false, true) { RequestId = "kurz" });
        Assert.True((await b.WaitForAsync<UserUpdated>()).User.SelfDeafened); // the long one never ran
    }

    [Fact]
    public void Outbox_ByteCap_Disconnects()
    {
        var session = new Session(1, "fp", "gast", IPAddress.Loopback, new byte[32], TimeProvider.System);
        var big = new ServerIcon(null, null, new string('A', 700_000));
        for (int i = 0; i < 20; i++) session.Outbox.Send(big);

        Assert.True(session.Outbox.Aborted.IsCancellationRequested);
        Assert.True(session.Outbox.QueuedBytes <= Limits.MaxOutboxBytes, $"{session.Outbox.QueuedBytes} bytes queued");
    }

    [Fact]
    public async Task NonReadingClient_DisconnectedAndSlotFreed()
    {
        var time = new ManualTimeProvider();
        await using var server = await TestServer.StartAsync(dataDir: NewDataDirWithIcon(), time: time,
            writeTimeout: TimeSpan.FromSeconds(1));
        await using var client = await TestClient.OpenAsync(server.Port);
        Assert.IsType<Welcome>(await client.HandshakeAsync("taub", pump: false));

        // About 700 KB per answer; far more than socket buffers and the outbox cap together.
        try
        {
            for (int i = 0; i < 60; i++)
            {
                time.Advance(Limits.IconInterval);
                await client.SendAsync(new GetServerIcon { RequestId = $"i{i}" });
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
        }

        var deadline = DateTime.UtcNow.AddSeconds(30); // returns early; a loaded CI runner can take long to notice
        while ((server.State.SessionCount > 0 || server.State.ConnectionsFrom(IPAddress.Loopback) > 0) && DateTime.UtcNow < deadline)
            await Task.Delay(50);
        Assert.Equal(0, server.State.SessionCount);
        Assert.Equal(0, server.State.ConnectionsFrom(IPAddress.Loopback));
        Assert.Contains(server.Log, l => l.Contains("taub getrennt (Sendepuffer voll)") || l.Contains("taub getrennt (Schreiben zu langsam)"));
    }

    [Fact]
    public async Task OverLimitConnection_ClosedBeforeTls()
    {
        await using var server = await TestServer.StartAsync();
        var open = new List<TestClient>();
        for (int i = 0; i < 5; i++) open.Add(await TestClient.OpenAsync(server.Port));

        await Assert.ThrowsAnyAsync<Exception>(() => TestClient.OpenAsync(server.Port));
        Assert.Contains(server.Log, l => l.Contains("abgelehnt: TooManyConnections"));
        foreach (var c in open) await c.DisposeAsync();
    }

    [Fact]
    public async Task PendingHandshakes_Capped()
    {
        await using var server = await TestServer.StartAsync(maxPendingHandshakes: 1);
        using var stalled = new TcpClient();
        await stalled.ConnectAsync(IPAddress.Loopback, server.Port); // no TLS: holds the only handshake slot
        // Wait until the accept loop has taken it (it counts the connection right before the slot), not a fixed time.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (server.State.ConnectionsFrom(IPAddress.Loopback) == 0 && DateTime.UtcNow < deadline) await Task.Delay(20);
        Assert.Equal(1, server.State.ConnectionsFrom(IPAddress.Loopback));
        await Task.Delay(100);

        await Assert.ThrowsAnyAsync<Exception>(() => TestClient.OpenAsync(server.Port));

        stalled.Close();
        TestClient? late = null;
        deadline = DateTime.UtcNow.AddSeconds(30);
        while (late is null && DateTime.UtcNow < deadline)
        {
            try
            {
                late = await TestClient.OpenAsync(server.Port);
            }
            catch (Exception e) when (e is IOException or System.Security.Authentication.AuthenticationException)
            {
                await Task.Delay(100);
            }
        }
        Assert.NotNull(late);
        await late.DisposeAsync();
    }
}
