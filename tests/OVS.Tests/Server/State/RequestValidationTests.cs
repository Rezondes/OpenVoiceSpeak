using OVS.Server;
using OVS.Server.Data;
using OVS.Server.Data.Records;
using OVS.Shared.Identity;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Chat;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.Groups;
using OVS.Shared.Protocol.Messages.ServerInfo;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server.State;

/// <summary>Package 83: every request is checked for null, range and allowed characters and answered, never dropped.</summary>
public sealed class RequestValidationTests
{
    static async Task<(TestServer Server, TestClient Admin)> StartWithAdminAsync(Action<ServerData>? seed = null)
    {
        var admin = ClientIdentity.Create();
        var server = await TestServer.StartAsync(serverData => { TestServer.Grant(admin, "Admin")(serverData); seed?.Invoke(serverData); });
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
        await using var client = admin;

        await admin.SendRawAsync(json.Replace("{lobby}", admin.Welcome.Snapshot.DefaultChannelId.ToString()));
        Assert.Equal(ReasonCodes.InvalidValue, (await admin.ErrorAsync("x")).Code);
        await AssertStillAnsweredAsync(admin);
        Assert.DoesNotContain(server.Log, line => line.Contains("Fehler bei Anfrage")); // checked, not caught
    }

    [Fact]
    public async Task Handle_HandlerThrows_AnsweredNotDropped()
    {
        var (server, admin) = await StartWithAdminAsync();
        await using var _ = server;
        await using var client = admin;
        server.State.BeforeRequest = request =>
        {
            if (request is CreateChannel) throw new InvalidOperationException("Testfehler");
        };

        await admin.SendAsync(new CreateChannel("Raid", "") { RequestId = "c" });
        Assert.Equal(ReasonCodes.InvalidValue, (await admin.ErrorAsync("c")).Code);
        await AssertStillAnsweredAsync(admin);
        Assert.Contains(server.Log, line => line.Contains("Fehler bei Anfrage CreateChannel") && line.Contains("Testfehler"));

        // the lock is free and the state usable
        server.State.BeforeRequest = null;
        await admin.SendAsync(new CreateChannel("Raid", ""));
        await admin.WaitForAsync<ChannelAdded>(channelAdded => channelAdded.Channel.Name == "Raid");
    }

    // A frame that is a JSON object but no valid request reaches the state without a request of its own.

    [Theory]
    [InlineData("r")]
    [InlineData(null)]
    public void HandleMalformed_CurrentSession_AnsweredInvalidValueWithTheProblem(string? requestId)
    {
        using var harness = new StateHarness();
        var anna = harness.Admit("anna");
        StateHarness.Drain(anna);

        harness.State.HandleMalformed(anna, requestId, "Unbekannter Typ");

        Assert.Equal(new Error(requestId, ReasonCodes.InvalidValue, "Unbekannter Typ"), Assert.Single(StateHarness.Drain(anna)));
    }

    [Fact]
    public void HandleMalformed_HookSet_HookNotCalled()
    {
        using var harness = new StateHarness();
        var anna = harness.Admit("anna");
        var hooked = new List<string?>();
        harness.State.BeforeRequest = request => hooked.Add(request.RequestId);

        harness.State.HandleMalformed(anna, "kaputt", "Unbekannter Typ");
        harness.Handle(anna, new JoinChannel(Guid.NewGuid()) { RequestId = "echt" });

        Assert.Equal(["echt"], hooked); // the hook runs for the request, not for the malformed frame
    }

    [Fact]
    public void HandleMalformed_Called_NothingLogged()
    {
        using var harness = new StateHarness();
        var anna = harness.Admit("anna");
        int logged = harness.Log.Count;

        harness.State.HandleMalformed(anna, "r", "Unbekannter Typ");

        Assert.Equal(logged, harness.Log.Count);
    }

    [Fact]
    public void HandleMalformed_LongRequestId_AnsweredWithTheCutId()
    {
        using var harness = new StateHarness();
        var anna = harness.Admit("anna");
        StateHarness.Drain(anna);
        var id = new string('x', ServerTuning.MaxRequestIdLength + 1);

        harness.State.HandleMalformed(anna, id, "Unbekannter Typ");

        var error = Assert.IsType<Error>(Assert.Single(StateHarness.Drain(anna)));
        Assert.Equal((id[..ServerTuning.MaxRequestIdLength], ReasonCodes.InvalidValue), (error.RequestId, error.Code));
    }

    [Fact]
    public void HandleMalformed_PastTheRequestBudget_RateLimited()
    {
        using var harness = new StateHarness();
        var anna = harness.Admit("anna");
        for (int index = 0; index < ServerTuning.RequestBurst; index++) harness.Handle(anna, new JoinChannel(Guid.NewGuid()));
        StateHarness.Drain(anna);

        harness.State.HandleMalformed(anna, "r", "Unbekannter Typ");

        Assert.Equal(new Error("r", ReasonCodes.RateLimited), Assert.Single(StateHarness.Drain(anna)));
    }

    [Fact]
    public void Handle_BadShapePastTheRequestBudget_RateLimitedBeforeTheShapeCheck()
    {
        using var harness = new StateHarness();
        var anna = harness.Admit("anna");
        for (int index = 0; index < ServerTuning.RequestBurst; index++) harness.Handle(anna, new JoinChannel(Guid.NewGuid()));
        StateHarness.Drain(anna);

        harness.Handle(anna, new CreateChannel("Raid", null!) { RequestId = "r" });

        Assert.Equal(new Error("r", ReasonCodes.RateLimited), Assert.Single(StateHarness.Drain(anna)));
    }

    [Theory]
    [InlineData("Admin\u200B")] // zero-width space
    [InlineData("\u202Eevil")] // right-to-left override
    [InlineData("a\u2028b")] // line separator
    [InlineData("a\u2029b")] // paragraph separator
    [InlineData("a\u0085b")] // C1 control
    [InlineData("a\u2066b")] // bidi isolate
    public async Task Names_FormatOrBidiChars_Rejected(string name)
    {
        var (server, admin) = await StartWithAdminAsync();
        await using var _ = server;
        await using var testClient = admin;

        var client = await TestClient.OpenAsync(server.Port);
        await using (client)
            Assert.Equal(ReasonCodes.NicknameInvalid, Assert.IsType<Rejected>(await client.HandshakeAsync(name, pump: false)).Code);

        await admin.SendAsync(new CreateChannel(name, "") { RequestId = "c" });
        Assert.Equal(ReasonCodes.InvalidName, (await admin.ErrorAsync("c")).Code);
        await admin.SendAsync(new CreateGroup(name, Permission.Speak) { RequestId = "g" });
        Assert.Equal(ReasonCodes.InvalidName, (await admin.ErrorAsync("g")).Code);
        await admin.SendAsync(new UpdateServerSettings(name, "", null) { RequestId = "s" });
        Assert.Equal(ReasonCodes.InvalidName, (await admin.ErrorAsync("s")).Code);
    }

    [Fact]
    public void Names_SpacesAndOrdinaryUnicode_TrimmedAndKept()
    {
        Assert.Equal("Raid", TextRules.NormalizeName("  Raid \u2028", 64));
        Assert.Equal("J\u00FCrgen \u00E9\u00E8", TextRules.NormalizeName("J\u00FCrgen \u00E9\u00E8", 32));
        Assert.Null(TextRules.NormalizeName("   ", 32));
        Assert.Null(TextRules.NormalizeName(new string('x', 33), 32));
        Assert.Null(TextRules.NormalizeName("a\uD800b", 32)); // lone surrogate
        Assert.Null(TextRules.NormalizeText("a\uD800b", 500));
        Assert.Equal("a\nb", TextRules.NormalizeText("a\r\nb", 500));
    }

    [Fact]
    public async Task Nickname_OfflineUserNameTaken_NormalizedCompare()
    {
        var owner = ClientIdentity.Create();
        var (server, admin) = await StartWithAdminAsync(serverData => serverData.Users.Add(new UserRecord
        {
            Fingerprint = owner.Fingerprint, LastNickname = "Boss", GroupIds = [WellKnownGroups.Guest],
        }));
        await using var _ = server;
        await using var client = admin;

        // the offline owner's name is not free for a new identity, in any case or compatibility form
        foreach (var name in new[] { "Boss", "boss", "\uFF42\uFF4F\uFF53\uFF53" }) // the last one fullwidth
        {
            var other = await TestClient.OpenAsync(server.Port);
            await using (other)
                Assert.Equal(ReasonCodes.NicknameTaken, Assert.IsType<Rejected>(await other.HandshakeAsync(name, pump: false)).Code);
        }

        // the owner gets it, and while online it is taken normalized as well
        await using var ownerClient = await TestClient.ConnectAsync(server, "Boss", owner);
        await using var anna = await TestClient.ConnectAsync(server, "anna");
        var copy = await TestClient.OpenAsync(server.Port);
        await using (copy)
            Assert.Equal(ReasonCodes.NicknameTaken, Assert.IsType<Rejected>(await copy.HandshakeAsync("\uFF21NNA", pump: false)).Code);
    }

    /// <summary>Old data can hold one stored name twice: it belongs to the record that logged in last.</summary>
    [Fact]
    public async Task Nickname_StoredTwice_ReservedForLatestLogin()
    {
        var now = DateTimeOffset.UtcNow;
        ClientIdentity clientIdentity = ClientIdentity.Create(), otherClientIdentity = ClientIdentity.Create(), never = ClientIdentity.Create(),
            neverMax = ClientIdentity.Create(), oldMax = ClientIdentity.Create();
        UserRecord Record(ClientIdentity id, string name, DateTimeOffset? lastLogin, DateTimeOffset firstSeen) => new()
        {
            Fingerprint = id.Fingerprint, LastNickname = name, GroupIds = [WellKnownGroups.Guest], LastLogin = lastLogin, FirstSeen = firstSeen,
        };
        var (server, admin) = await StartWithAdminAsync(serverData => serverData.Users.AddRange(
        [
            Record(clientIdentity, "Steven", now.AddDays(-1), now.AddDays(-30)),
            Record(otherClientIdentity, "Steven", now, now.AddDays(-2)),
            Record(never, "steven", null, now.AddDays(-60)),
            Record(neverMax, "Max", null, now.AddDays(-60)),
            Record(oldMax, "Max", now.AddDays(-60), now.AddDays(-60)),
        ]));
        await using var _ = server;
        await using var adminClient = admin;

        async Task Refused(ClientIdentity? id, string name)
        {
            var client = await TestClient.OpenAsync(server.Port, id);
            await using (client)
                Assert.Equal(ReasonCodes.NicknameTaken, Assert.IsType<Rejected>(await client.HandshakeAsync(name, pump: false)).Code);
        }

        await using (await TestClient.ConnectAsync(server, "Steven", otherClientIdentity)) { }
        await Refused(clientIdentity, "Steven");
        await Refused(null, "Steven");
        await Refused(never, "Steven"); // without a login it counts as the oldest

        await Refused(neverMax, "Max");
        await using (await TestClient.ConnectAsync(server, "Max", oldMax)) { }
    }

    [Fact]
    public async Task MultilineTexts_Newline_AllowedOtherControlsRejected()
    {
        var (server, admin) = await StartWithAdminAsync();
        await using var _ = server;
        await using var client = admin;

        await admin.SendAsync(new UpdateServerSettings("Testserver", "Zeile 1\r\nZeile 2", null));
        Assert.Equal("Zeile 1\nZeile 2", (await admin.WaitForAsync<ServerSettingsChanged>()).Settings.WelcomeText);
        await admin.SendAsync(new CreateChannel("Raid", "oben\nunten"));
        Assert.Equal("oben\nunten", (await admin.WaitForAsync<ChannelAdded>()).Channel.Description);
        await admin.SendAsync(new SendChat(ChatTarget.Server, null, "a\nb \U0001F468\u200D\U0001F469")); // a ZWJ emoji is fine in texts
        Assert.Equal("a\nb \U0001F468\u200D\U0001F469", (await admin.WaitForAsync<ChatMessage>()).Text);

        int count = 0;
        foreach (var bad in new[] { "a\u0007b", "a\u0085b", "a\u202Eb", "a\u200Fb", "a\u2028b", "a\rb", "a\tb" })
        {
            await admin.SendAsync(new UpdateServerSettings("Testserver", bad, null) { RequestId = $"w{count}" });
            Assert.Equal(ReasonCodes.InvalidValue, (await admin.ErrorAsync($"w{count}")).Code);
            await admin.SendAsync(new CreateChannel($"Kanal {count}", bad) { RequestId = $"d{count}" });
            Assert.Equal(ReasonCodes.InvalidValue, (await admin.ErrorAsync($"d{count}")).Code);
            await admin.SendAsync(new SendChat(ChatTarget.Channel, null, bad) { RequestId = $"c{count}" });
            Assert.Equal(ReasonCodes.InvalidValue, (await admin.ErrorAsync($"c{count}")).Code);
            count++;
        }
        await admin.AssertNoMessageAsync<ChatMessage>();
        await admin.AssertNoMessageAsync<ChannelAdded>();
    }

    [Fact]
    public async Task ChannelOrder_NoOverflow_PasswordLength()
    {
        var far = Guid.NewGuid();
        var (server, admin) = await StartWithAdminAsync(serverData => serverData.Channels.Add(new ChannelRecord { Id = far, Name = "Fern", Order = int.MaxValue }));
        await using var _ = server;
        await using var client = admin;
        var lobby = admin.Welcome.Snapshot.Channels.Single(channel => channel.Id == admin.Welcome.Snapshot.DefaultChannelId);

        // EditChannel keeps the order, that only changes through ReorderChannels
        await admin.SendAsync(new EditChannel(lobby.Id, "Lobby", "", int.MaxValue - 5));
        Assert.Equal(lobby.Order, (await admin.WaitForAsync<ChannelUpdated>()).Channel.Order);

        // a stored order at the top does not wrap around
        await admin.SendAsync(new CreateChannel("Neu", ""));
        Assert.Equal(int.MaxValue, (await admin.WaitForAsync<ChannelAdded>()).Channel.Order);

        await admin.SendAsync(new UpdateServerSettings("Testserver", "", new string('p', 129)) { RequestId = "long" });
        Assert.Equal(ReasonCodes.InvalidValue, (await admin.ErrorAsync("long")).Code);
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
    public async Task SendChat_TargetOutOfRange_Rejected()
    {
        var (server, admin) = await StartWithAdminAsync();
        await using var _ = server;
        await using var client = admin;

        for (int index = 0; index <= ServerTuning.ChatBurst; index++)
        {
            await admin.SendRawAsync($$"""{"type":"sendChat","requestId":"t{{index}}","target":7,"toSessionId":null,"text":"hallo"}""");
            Assert.Equal(ReasonCodes.InvalidValue, (await admin.ErrorAsync($"t{index}")).Code);
        }

        // none of them counted against the chat limit
        await admin.SendAsync(new SendChat(ChatTarget.Server, null, "geht"));
        Assert.Equal("geht", (await admin.WaitForAsync<ChatMessage>()).Text);
    }

    /// <summary>Package 83: a restore keeps the looser rule of stored names, so older backups stay usable.</summary>
    [Fact]
    public void BackupValidation_StoredNamesNewInputWouldRefuse_Kept()
    {
        var data = ServerData.CreateDefault(new ServerConfig(0, "unused", 10, "Testserver", ""));
        data.Users.Add(new UserRecord { Fingerprint = "f", LastNickname = "Alt\u200B", GroupIds = [WellKnownGroups.Admin] });
        data.Channels.Add(new ChannelRecord { Id = Guid.NewGuid(), Name = "Raid\u202E", Order = 1 });
        BackupArchive.Validate(data);

        data.Channels.Add(new ChannelRecord { Id = Guid.NewGuid(), Name = "Kaputt\u0001", Order = 2 });
        Assert.Throws<InvalidDataException>(() => BackupArchive.Validate(data));
    }
}
