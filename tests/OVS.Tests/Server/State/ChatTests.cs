using OVS.Server;
using OVS.Server.Data.Records;
using OVS.Shared.Identity;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Chat;
using OVS.Shared.Protocol.Messages.Moderation;
using OVS.Shared.Protocol.Messages.Users;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server.State;

/// <summary>Package 31: chat delivery, rights, limits and logs on the server.</summary>
public sealed class ChatTests
{
    /// <summary>chef (admin) and gast in the lobby, weit (guest) in Raid.</summary>
    static async Task<(TestServer Server, TestClient Chef, TestClient Gast, TestClient Weit, Guid Raid)> StartAsync(TimeProvider? time = null)
    {
        var chefId = ClientIdentity.Create();
        var raid = Guid.NewGuid();
        var server = await TestServer.StartAsync(serverData =>
        {
            TestServer.Grant(chefId, "Admin")(serverData);
            serverData.Channels.Add(new ChannelRecord { Id = raid, Name = "Raid", Order = 1 });
        }, time: time);
        var chef = await TestClient.ConnectAsync(server, "chef", chefId);
        var gast = await TestClient.ConnectAsync(server, "gast");
        var weit = await TestClient.ConnectAsync(server, "weit");
        await weit.SendAsync(new JoinChannel(raid));
        await chef.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == weit.Id && userUpdated.User.ChannelId == raid);
        return (server, chef, gast, weit, raid);
    }

    [Fact]
    public async Task Deliver_ServerChannelPrivate()
    {
        var (server, chef, gast, weit, _) = await StartAsync();
        await using var _s = server;
        await using var _c = chef;
        await using var _g = gast;
        await using var _w = weit;

        await chef.SendAsync(new SendChat(ChatTarget.Server, null, "  Hallo alle  "));
        foreach (var client in new[] { chef, gast, weit })
        {
            var chatMessage = await client.WaitForAsync<ChatMessage>(otherChatMessage => otherChatMessage.Target == ChatTarget.Server);
            Assert.Equal(("chef", "Hallo alle"), (chatMessage.FromNickname, chatMessage.Text)); // trimmed
        }

        await gast.SendAsync(new SendChat(ChatTarget.Channel, null, "nur Lobby"));
        Assert.Equal("nur Lobby", (await chef.WaitForAsync<ChatMessage>(chatMessage => chatMessage.Target == ChatTarget.Channel)).Text);
        await weit.AssertNoMessageAsync<ChatMessage>(); // other channel

        await gast.SendAsync(new SendChat(ChatTarget.Private, weit.Id, "psst"));
        var whisper = await weit.WaitForAsync<ChatMessage>(chatMessage => chatMessage.Target == ChatTarget.Private);
        Assert.Equal((gast.Id, weit.Id, "psst"), (whisper.FromSessionId, whisper.ToSessionId!.Value, whisper.Text));
        await gast.WaitForAsync<ChatMessage>(chatMessage => chatMessage.Target == ChatTarget.Private); // the sender sees it too
        await chef.AssertNoMessageAsync<ChatMessage>();
    }

    public static TheoryData<ChatTarget> Targets => new() { ChatTarget.Server, ChatTarget.Channel, ChatTarget.Private };

    [Theory]
    [MemberData(nameof(Targets))]
    public async Task NoRight_PermissionDenied_NobodyGetsIt(ChatTarget target)
    {
        var right = target switch { ChatTarget.Server => Permission.ChatServer, ChatTarget.Channel => Permission.ChatChannel, _ => Permission.ChatPrivate };
        await using var server = await TestServer.StartAsync(serverData =>
        {
            var guest = serverData.Groups.FindIndex(groupRecord => groupRecord.Id == WellKnownGroups.Guest);
            serverData.Groups[guest] = serverData.Groups[guest] with { Permissions = serverData.Groups[guest].Permissions & ~right };
        });
        await using var sender = await TestClient.ConnectAsync(server, "ohne");
        await using var other = await TestClient.ConnectAsync(server, "andere");

        await sender.SendAsync(new SendChat(target, other.Id, "hallo") { RequestId = "c" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await sender.ErrorAsync("c")).Code);
        await other.AssertNoMessageAsync<ChatMessage>();
    }

    [Fact]
    public async Task Invalid_EmptyTooLongOfflineSelf()
    {
        var (server, chef, gast, weit, _) = await StartAsync();
        await using var _s = server;
        await using var _c = chef;
        await using var _g = gast;
        await using var _w = weit;

        async Task<string> Code(SendChat request, string id)
        {
            await gast.SendAsync(request with { RequestId = id });
            return (await gast.ErrorAsync(id)).Code;
        }
        Assert.Equal(ReasonCodes.InvalidValue, await Code(new SendChat(ChatTarget.Channel, null, "   "), "leer"));
        Assert.Equal(ReasonCodes.InvalidValue, await Code(new SendChat(ChatTarget.Channel, null, new string('x', ProtocolLimits.MaxChatLength + 1)), "lang"));
        Assert.Equal(ReasonCodes.NotFound, await Code(new SendChat(ChatTarget.Private, 999, "hallo"), "offline"));
        Assert.Equal(ReasonCodes.InvalidValue, await Code(new SendChat(ChatTarget.Private, gast.Id, "ich"), "selbst"));
        await gast.SendAsync(new SendChat(ChatTarget.Channel, null, new string('x', ProtocolLimits.MaxChatLength)));
        await chef.WaitForAsync<ChatMessage>(); // exactly the limit is fine
    }

    [Fact]
    public async Task RateLimit_SixthInFiveSeconds()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var (server, chef, gast, weit, _) = await StartAsync(time);
        await using var _s = server;
        await using var _c = chef;
        await using var _g = gast;
        await using var _w = weit;

        for (int index = 0; index < ServerTuning.ChatBurst; index++) await gast.SendAsync(new SendChat(ChatTarget.Channel, null, $"n{index}"));
        await gast.SendAsync(new SendChat(ChatTarget.Channel, null, "zu viel") { RequestId = "sechs" });
        Assert.Equal(ReasonCodes.RateLimited, (await gast.ErrorAsync("sechs")).Code);

        time.Advance(ServerTuning.ChatWindow);
        await gast.SendAsync(new SendChat(ChatTarget.Channel, null, "wieder da"));
        await chef.WaitForAsync<ChatMessage>(chatMessage => chatMessage.Text == "wieder da");
    }

    [Fact]
    public async Task ServerMuted_CanWrite()
    {
        var (server, chef, gast, weit, _) = await StartAsync();
        await using var _s = server;
        await using var _c = chef;
        await using var _g = gast;
        await using var _w = weit;

        await chef.SendAsync(new SetSessionServerMute(gast.Id, true));
        await gast.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == gast.Id && userUpdated.User.ServerMuted);
        await gast.SendAsync(new SendChat(ChatTarget.Channel, null, "stumm, aber lesbar"));
        await chef.WaitForAsync<ChatMessage>(chatMessage => chatMessage.Text == "stumm, aber lesbar");
    }

    [Fact]
    public async Task Logs_ServerAndChannel_PrivateWithoutContent()
    {
        var (server, chef, gast, weit, raid) = await StartAsync();
        await using var _s = server;
        await using var _c = chef;
        await using var _g = gast;
        await using var _w = weit;

        await chef.SendAsync(new SendChat(ChatTarget.Server, null, "Ankündigung"));
        await weit.SendAsync(new SendChat(ChatTarget.Channel, null, "im Raid"));
        await gast.SendAsync(new SendChat(ChatTarget.Private, weit.Id, "geheimer Inhalt"));
        // three senders, three read loops on the server, in any order: wait for all three messages, not only the
        // last one sent (each is logged before it goes out)
        var seen = new HashSet<string>();
        while (seen.Count < 3)
            seen.Add((await weit.WaitForAsync<ChatMessage>(chatMessage => chatMessage.Text is "Ankündigung" or "im Raid" or "geheimer Inhalt")).Text);

        Assert.Contains(server.Log, line => line.Contains("Chat von chef: Ankündigung"));
        Assert.Contains(server.Log, line => line.Contains("gast schreibt privat an weit"));
        Assert.DoesNotContain(server.Log, line => line.Contains("geheimer Inhalt"));
        var files = Directory.GetFiles(Path.Combine(server.DataDir, "logs"), "*.log", SearchOption.AllDirectories)
            .Select(path =>
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return (Path: path, Text: new StreamReader(stream).ReadToEnd());
            }).ToList();
        Assert.Contains(files, f => f.Path.Contains(raid.ToString()) && f.Text.Contains("[Raid] weit: im Raid"));
        Assert.DoesNotContain(files, f => f.Text.Contains("geheimer Inhalt"));
    }
}
