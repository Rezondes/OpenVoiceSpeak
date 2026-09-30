using OVS.Server;
using OVS.Server.Data;
using OVS.Shared.Identity;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server;

/// <summary>Package 83: every request is checked for null, range and allowed characters and answered, never dropped.</summary>
public sealed class RequestValidationTests
{
    static async Task<(TestServer Server, TestClient Admin)> StartWithAdminAsync(Action<ServerData>? seed = null)
    {
        var admin = ClientIdentity.Create();
        var server = await TestServer.StartAsync(d => { TestServer.Grant(admin, "Admin")(d); seed?.Invoke(d); });
        return (server, await TestClient.ConnectAsync(server, "chef", admin));
    }

    static async Task AssertStillAnsweredAsync(TestClient client)
    {
        await client.SendAsync(new Ping());
        await client.WaitForAsync<Pong>();
    }

    [Theory]
    [InlineData("""{"type":"createChannel","requestId":"x","name":"Raid","description":null}""")]
    [InlineData("""{"type":"createChannel","requestId":"x","name":"Raid"}""")]
    [InlineData("""{"type":"editChannel","requestId":"x","channelId":"{lobby}","name":"Lobby","description":null,"order":0}""")]
    [InlineData("""{"type":"uploadBackupChunk","requestId":"x","uploadId":"0123456789abcdef0123456789abcdef","offset":0,"dataBase64":null,"isLast":false}""")]
    [InlineData("""{"type":"setChannelLinks","requestId":"x","add":[null],"remove":[]}""")]
    [InlineData("""{"type":"sendChat","requestId":"x","target":"Server","toSessionId":null,"text":null}""")]
    [InlineData("""{"type":"sendChat","requestId":"x","target":"Nowhere","toSessionId":null,"text":"hallo"}""")]
    [InlineData("""{"type":"searchLogs","requestId":"x","query":"a","kind":9}""")]
    [InlineData("""{"type":"prepareLogDownload","requestId":"x","fileIds":[null]}""")]
    [InlineData("""{"type":"reorderChannels","requestId":"x","channelIds":5}""")]
    [InlineData("""{"type":"updateServerSettings","requestId":"x","name":null,"welcomeText":"","password":null}""")]
    [InlineData("""{"type":"noSuchRequest","requestId":"x"}""")]
    public async Task NullFields_Rejected_ConnectionStays(string json)
    {
        var (server, admin) = await StartWithAdminAsync();
        await using var _ = server;
        await using var a = admin;

        await admin.SendRawAsync(json.Replace("{lobby}", admin.Welcome.Snapshot.DefaultChannelId.ToString()));
        Assert.Equal(Codes.InvalidValue, (await admin.ErrorAsync("x")).Code);
        await AssertStillAnsweredAsync(admin);
        Assert.DoesNotContain(server.Log, l => l.Contains("Fehler bei Anfrage")); // checked, not caught
    }

    [Fact]
    public async Task HandlerException_AnsweredNotDropped()
    {
        var (server, admin) = await StartWithAdminAsync();
        await using var _ = server;
        await using var a = admin;
        server.State.BeforeRequest = r =>
        {
            if (r is CreateChannel) throw new InvalidOperationException("Testfehler");
        };

        await admin.SendAsync(new CreateChannel("Raid", "") { RequestId = "c" });
        Assert.Equal(Codes.InvalidValue, (await admin.ErrorAsync("c")).Code);
        await AssertStillAnsweredAsync(admin);
        Assert.Contains(server.Log, l => l.Contains("Fehler bei Anfrage CreateChannel") && l.Contains("Testfehler"));

        // the lock is free and the state usable
        server.State.BeforeRequest = null;
        await admin.SendAsync(new CreateChannel("Raid", ""));
        await admin.WaitForAsync<ChannelAdded>(c => c.Channel.Name == "Raid");
    }

    [Theory]
    [InlineData("Admin\u200B")] // zero-width space
    [InlineData("\u202Eevil")] // right-to-left override
    [InlineData("a\u2028b")] // line separator
    [InlineData("a\u2029b")] // paragraph separator
    [InlineData("a\u0085b")] // C1 control
    [InlineData("a\u2066b")] // bidi isolate
    public async Task Names_RejectFormatAndBidiChars(string name)
    {
        var (server, admin) = await StartWithAdminAsync();
        await using var _ = server;
        await using var a = admin;

        var client = await TestClient.OpenAsync(server.Port);
        await using (client)
            Assert.Equal(Codes.NicknameInvalid, Assert.IsType<Rejected>(await client.HandshakeAsync(name, pump: false)).Code);

        await admin.SendAsync(new CreateChannel(name, "") { RequestId = "c" });
        Assert.Equal(Codes.InvalidName, (await admin.ErrorAsync("c")).Code);
        await admin.SendAsync(new CreateGroup(name, Permission.Speak) { RequestId = "g" });
        Assert.Equal(Codes.InvalidName, (await admin.ErrorAsync("g")).Code);
        await admin.SendAsync(new UpdateServerSettings(name, "", null) { RequestId = "s" });
        Assert.Equal(Codes.InvalidName, (await admin.ErrorAsync("s")).Code);
    }

    [Fact]
    public void Names_TrimmedAndOrdinaryUnicodeKept()
    {
        Assert.Equal("Raid", TextRules.Name("  Raid \u2028", 64));
        Assert.Equal("J\u00FCrgen \u00E9\u00E8", TextRules.Name("J\u00FCrgen \u00E9\u00E8", 32));
        Assert.Null(TextRules.Name("   ", 32));
        Assert.Null(TextRules.Name(new string('x', 33), 32));
        Assert.Null(TextRules.Name("a\uD800b", 32)); // lone surrogate
        Assert.Null(TextRules.Text("a\uD800b", 500));
        Assert.Equal("a\nb", TextRules.Text("a\r\nb", 500));
    }

    [Fact]
    public async Task Nickname_OfflineUserNameTaken_NormalizedCompare()
    {
        var owner = ClientIdentity.Create();
        var (server, admin) = await StartWithAdminAsync(d => d.Users.Add(new UserRecord
        {
            Fingerprint = owner.Fingerprint, LastNickname = "Boss", GroupIds = [WellKnownGroups.Guest],
        }));
        await using var _ = server;
        await using var a = admin;

        // the offline owner's name is not free for a new identity, in any case or compatibility form
        foreach (var name in new[] { "Boss", "boss", "\uFF42\uFF4F\uFF53\uFF53" }) // the last one fullwidth
        {
            var other = await TestClient.OpenAsync(server.Port);
            await using (other)
                Assert.Equal(Codes.NicknameTaken, Assert.IsType<Rejected>(await other.HandshakeAsync(name, pump: false)).Code);
        }

        // the owner gets it, and while online it is taken normalized as well
        await using var ownerClient = await TestClient.ConnectAsync(server, "Boss", owner);
        await using var anna = await TestClient.ConnectAsync(server, "anna");
        var copy = await TestClient.OpenAsync(server.Port);
        await using (copy)
            Assert.Equal(Codes.NicknameTaken, Assert.IsType<Rejected>(await copy.HandshakeAsync("\uFF21NNA", pump: false)).Code);
    }

    [Fact]
    public async Task MultilineTexts_AllowNewlineRejectOtherControls()
    {
        var (server, admin) = await StartWithAdminAsync();
        await using var _ = server;
        await using var a = admin;

        await admin.SendAsync(new UpdateServerSettings("Testserver", "Zeile 1\r\nZeile 2", null));
        Assert.Equal("Zeile 1\nZeile 2", (await admin.WaitForAsync<ServerSettingsChanged>()).Settings.WelcomeText);
        await admin.SendAsync(new CreateChannel("Raid", "oben\nunten"));
        Assert.Equal("oben\nunten", (await admin.WaitForAsync<ChannelAdded>()).Channel.Description);
        await admin.SendAsync(new SendChat(ChatTarget.Server, null, "a\nb \U0001F468\u200D\U0001F469")); // a ZWJ emoji is fine in texts
        Assert.Equal("a\nb \U0001F468\u200D\U0001F469", (await admin.WaitForAsync<ChatMessage>()).Text);

        int n = 0;
        foreach (var bad in new[] { "a\u0007b", "a\u0085b", "a\u202Eb", "a\u200Fb", "a\u2028b", "a\rb", "a\tb" })
        {
            await admin.SendAsync(new UpdateServerSettings("Testserver", bad, null) { RequestId = $"w{n}" });
            Assert.Equal(Codes.InvalidValue, (await admin.ErrorAsync($"w{n}")).Code);
            await admin.SendAsync(new CreateChannel($"Kanal {n}", bad) { RequestId = $"d{n}" });
            Assert.Equal(Codes.InvalidValue, (await admin.ErrorAsync($"d{n}")).Code);
            await admin.SendAsync(new SendChat(ChatTarget.Channel, null, bad) { RequestId = $"c{n}" });
            Assert.Equal(Codes.InvalidValue, (await admin.ErrorAsync($"c{n}")).Code);
            n++;
        }
        await admin.AssertNoMessageAsync<ChatMessage>();
        await admin.AssertNoMessageAsync<ChannelAdded>();
    }

    [Fact]
    public async Task ChannelOrder_NoOverflow_PasswordLength()
    {
        var far = Guid.NewGuid();
        var (server, admin) = await StartWithAdminAsync(d => d.Channels.Add(new ChannelRecord { Id = far, Name = "Fern", Order = int.MaxValue }));
        await using var _ = server;
        await using var a = admin;
        var lobby = admin.Welcome.Snapshot.Channels.Single(c => c.Id == admin.Welcome.Snapshot.DefaultChannelId);

        // EditChannel keeps the order, that only changes through ReorderChannels
        await admin.SendAsync(new EditChannel(lobby.Id, "Lobby", "", int.MaxValue - 5));
        Assert.Equal(lobby.Order, (await admin.WaitForAsync<ChannelUpdated>()).Channel.Order);

        // a stored order at the top does not wrap around
        await admin.SendAsync(new CreateChannel("Neu", ""));
        Assert.Equal(int.MaxValue, (await admin.WaitForAsync<ChannelAdded>()).Channel.Order);

        await admin.SendAsync(new UpdateServerSettings("Testserver", "", new string('p', 129)) { RequestId = "long" });
        Assert.Equal(Codes.InvalidValue, (await admin.ErrorAsync("long")).Code);
        Assert.False((await ConnectedSettingsAsync(server)).HasPassword);
        await admin.SendAsync(new UpdateServerSettings("Testserver", "", new string('p', 128)));
        Assert.True((await admin.WaitForAsync<ServerSettingsChanged>()).Settings.HasPassword);
        await admin.SendAsync(new UpdateServerSettings("Testserver", "", ""));
        Assert.False((await admin.WaitForAsync<ServerSettingsChanged>()).Settings.HasPassword);
    }

    static async Task<ServerSettingsInfo> ConnectedSettingsAsync(TestServer server)
    {
        await using var probe = await TestClient.ConnectAsync(server);
        return probe.Welcome.Snapshot.Settings;
    }

    [Fact]
    public async Task ChatTargetOutOfRange_Rejected()
    {
        var (server, admin) = await StartWithAdminAsync();
        await using var _ = server;
        await using var a = admin;

        for (int i = 0; i <= ProtocolInfo.ChatBurst; i++)
        {
            await admin.SendRawAsync($$"""{"type":"sendChat","requestId":"t{{i}}","target":7,"toSessionId":null,"text":"hallo"}""");
            Assert.Equal(Codes.InvalidValue, (await admin.ErrorAsync($"t{i}")).Code);
        }

        // none of them counted against the chat limit
        await admin.SendAsync(new SendChat(ChatTarget.Server, null, "geht"));
        Assert.Equal("geht", (await admin.WaitForAsync<ChatMessage>()).Text);
    }

    /// <summary>Package 83: a restore keeps the looser rule of stored names, so older backups stay usable.</summary>
    [Fact]
    public void BackupValidation_KeepsStoredNamesThatNewInputWouldRefuse()
    {
        var data = ServerData.CreateDefault(new ServerConfig(0, "unused", 10, "Testserver", ""));
        data.Users.Add(new UserRecord { Fingerprint = "f", LastNickname = "Alt\u200B", GroupIds = [WellKnownGroups.Admin] });
        data.Channels.Add(new ChannelRecord { Id = Guid.NewGuid(), Name = "Raid\u202E", Order = 1 });
        BackupStore.Validate(data);

        data.Channels.Add(new ChannelRecord { Id = Guid.NewGuid(), Name = "Kaputt\u0001", Order = 2 });
        Assert.Throws<InvalidDataException>(() => BackupStore.Validate(data));
    }
}
