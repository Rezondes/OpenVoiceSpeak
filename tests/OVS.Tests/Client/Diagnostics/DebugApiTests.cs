using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using OVS.Client.Diagnostics;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Diagnostics;

/// <summary>
/// Two real clients (no audio devices, test tone instead of a microphone) driven only through the debug API,
/// talking through a real server over TLS and UDP.
/// </summary>
public sealed class DebugApiTests : IAsyncLifetime
{
    TestServer server = null!;
    Instance anna = null!, bert = null!;

    public async Task InitializeAsync()
    {
        server = await TestServer.StartAsync();
        anna = await Instance.StartAsync();
        bert = await Instance.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await anna.DisposeAsync();
        await bert.DisposeAsync();
        await server.DisposeAsync();
    }

    static IEnumerable<JsonElement> Channels(JsonElement state) => state.GetProperty("server").GetProperty("channels").EnumerateArray();

    static JsonElement Channel(JsonElement state, string name) => Channels(state).Single(element => element.GetProperty("name").GetString() == name);

    static JsonElement? User(JsonElement state, uint id) =>
        Channels(state).SelectMany(element => element.GetProperty("users").EnumerateArray())
            .Cast<JsonElement?>().FirstOrDefault(jsonElement => jsonElement!.Value.GetProperty("sessionId").GetUInt32() == id);

    static long FramesFrom(JsonElement state, uint speaker) =>
        state.GetProperty("audio").GetProperty("framesReceived").TryGetProperty(speaker.ToString(), out var element) ? element.GetInt64() : 0;

    static uint SelfId(JsonElement state) => state.GetProperty("server").GetProperty("selfId").GetUInt32();

    object ConnectBody(string nickname) => new { host = "127.0.0.1", port = server.Port, nickname };

    [Fact]
    public async Task Request_MissingHeader_Forbidden()
    {
        using var plain = new HttpClient();
        var response = await plain.GetAsync($"http://localhost:{anna.Port}/state");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>Package 129: a body without "vadAutomatic" is read like an old file: a threshold given by hand stays manual.</summary>
    [Fact]
    public async Task Settings_WithoutVadAutomatic_ManualUnlessDefault()
    {
        Assert.False((await anna.Post("settings", new { vadThresholdDb = -50 })).GetProperty("vadAutomatic").GetBoolean());
        Assert.True((await anna.Post("settings", new { vadThresholdDb = -40 })).GetProperty("vadAutomatic").GetBoolean());
        Assert.True((await anna.Post("settings", new { vadThresholdDb = -50, vadAutomatic = true })).GetProperty("vadAutomatic").GetBoolean());
    }

    [Fact]
    public async Task Devices_ViewModelWithoutAudio_ListsTheHardwaresDevices()
    {
        var devices = JsonDocument.Parse(await anna.Http.GetStringAsync("devices")).RootElement;
        Assert.Equal("mic-1", devices.GetProperty("inputs").EnumerateArray().Single().GetProperty("id").GetString());
        Assert.Equal("Speakers", devices.GetProperty("outputs").EnumerateArray().Single().GetProperty("name").GetString());
    }

    [Fact]
    public async Task Join_UnknownChannel_BadRequest()
    {
        await anna.Post("connect", ConnectBody("anna"));
        var response = await anna.Http.PostAsJsonAsync("join", new { channel = "gibtsnicht" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnknownPost_Connected_NamesTheEndpoint()
    {
        await anna.Post("connect", ConnectBody("anna"));

        var response = await anna.Http.PostAsJsonAsync("gibtsnicht", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Unbekannter Endpunkt POST /gibtsnicht", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnknownPost_NotConnected_SaysNotConnected()
    {
        var response = await anna.Http.PostAsJsonAsync("gibtsnicht", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Nicht verbunden", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Get_UnknownPath_IsBadRequest()
    {
        var response = await anna.Http.GetAsync("join");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Unbekannter Endpunkt GET /join", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ServerRoute_NotConnected_SaysNotConnected()
    {
        var response = await anna.Http.PostAsJsonAsync("mute", new { value = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Nicht verbunden", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Tone_NotConnected_ReportsTheTone()
    {
        var reply = await anna.Post("tone", new { hz = 440 });

        Assert.Equal(440, reply.GetProperty("toneHz").GetDouble());
    }

    [Fact]
    public async Task Disconnect_Connected_StateSaysNotConnected()
    {
        await anna.Post("connect", ConnectBody("anna"));

        var state = await anna.Post("disconnect");

        Assert.False(state.GetProperty("connected").GetBoolean());
    }

    [Fact]
    public async Task State_ConnectedAndPinged_CarriesThePingText()
    {
        await anna.Post("connect", ConnectBody("anna"));

        var state = await anna.Until(reported => reported.GetProperty("pingText").GetString()!.Length > 0);

        Assert.StartsWith("Ping ", state.GetProperty("pingText").GetString());
    }

    /// <summary>Package 110: a name two channels carry is refused, naming both ids; the id still works.</summary>
    [Fact]
    public async Task ChannelName_Ambiguous_RefusedNamingBothIds()
    {
        await anna.Post("connect", ConnectBody("anna"));
        await anna.Post("redeem", new { token = server.State.PendingAdminToken });
        await anna.Until(element => element.GetProperty("server").GetProperty("isAdmin").GetBoolean());
        await anna.Post("create-channel", new { name = "Raid" });
        await anna.Post("create-channel", new { name = "raid" });
        var state = await anna.Until(element => Channels(element).Count(otherElement => string.Equals(otherElement.GetProperty("name").GetString(), "raid", StringComparison.OrdinalIgnoreCase)) == 2);
        var response = await anna.Http.PostAsJsonAsync("join", new { channel = "Raid" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        var ids = Channels(state).Where(element => string.Equals(element.GetProperty("name").GetString(), "raid", StringComparison.OrdinalIgnoreCase))
            .Select(element => element.GetProperty("id").GetString()!).ToList();
        Assert.All(ids, id => Assert.Contains(id, text));
        await anna.Post("join", new { channel = ids[0] });
        Assert.All(Channels(state), element => Assert.False(element.GetProperty("isSeparator").GetBoolean()));

        // Package 111: the state names separators
        await anna.Post("request", new { type = "createChannel", name = "", description = "", kind = "Separator" });
        await anna.Until(element => Channels(element).Any(otherElement => otherElement.GetProperty("isSeparator").GetBoolean() && otherElement.GetProperty("name").GetString() == ""));
        await anna.Until(element => Channels(element).Single(otherElement => otherElement.GetProperty("id").GetString() == ids[0]).GetProperty("isCurrent").GetBoolean());
    }

    [Fact]
    public async Task FullFlow_Linking_PttAndLinkPtt()
    {
        var element = await anna.Post("connect", ConnectBody("anna"));
        Assert.True(element.GetProperty("connected").GetBoolean(), element.ToString());
        var annaId = SelfId(element);
        await bert.Post("connect", ConnectBody("bert"));

        // anna becomes admin, creates Raid; bert moves there
        await anna.Post("redeem", new { token = server.State.PendingAdminToken });
        await anna.Until(otherElement => otherElement.GetProperty("server").GetProperty("isAdmin").GetBoolean());
        await anna.Post("create-channel", new { name = "Raid" });
        await anna.Until(otherElement => Channels(otherElement).Any(value => value.GetProperty("name").GetString() == "Raid"));
        await bert.Post("join", new { channel = "Raid" });
        await bert.Until(otherElement => Channel(otherElement, "Raid").GetProperty("isCurrent").GetBoolean());

        // Normal PTT from the lobby does not reach Raid
        await anna.Post("tone", new { hz = 440 });
        await anna.Post("ptt", new { down = true });
        await anna.Until(otherElement => otherElement.GetProperty("audio").GetProperty("framesSent").GetInt64() > 10);
        await AsyncWait.AssertStaysFalseAsync(async () => FramesFrom(await bert.State(), annaId) > 0, TimeSpan.FromMilliseconds(300), "bert in another channel hears nothing");
        Assert.Equal(0, FramesFrom(await bert.State(), annaId));
        await anna.Post("ptt", new { down = false });

        // Link Lobby and Raid: normal PTT still stays in the lobby ...
        await anna.Post("link", new { a = "Lobby", b = "Raid" });
        await bert.Until(otherElement => Channel(otherElement, "Raid").GetProperty("isLinked").GetBoolean());
        long sentUnlinked = (await anna.State()).GetProperty("audio").GetProperty("framesSent").GetInt64();
        await anna.Post("ptt", new { down = true });
        await anna.Until(otherElement => otherElement.GetProperty("audio").GetProperty("framesSent").GetInt64() > sentUnlinked + 10); // she really talks
        await AsyncWait.AssertStaysFalseAsync(async () => FramesFrom(await bert.State(), annaId) > 0, TimeSpan.FromMilliseconds(300), "plain push to talk stays in the own channel");
        Assert.Equal(0, FramesFrom(await bert.State(), annaId));
        await anna.Post("ptt", new { down = false });

        // ... but link PTT reaches Raid, marked as link transmission
        await anna.Post("linkptt", new { down = true });
        var bertView = await bert.Until(otherElement => FramesFrom(otherElement, annaId) > 10 && User(otherElement, annaId)?.GetProperty("isSpeakingViaLink").GetBoolean() == true);
        Assert.Contains("Links", (await anna.State()).GetProperty("transmitText").GetString());
        // The indicator must stay on while anna keeps talking (it used to go dark for good after 300 ms).
        // Polled, because a starved test machine may drop it for a moment; gone for good still fails. The 700 ms are the
        // point of the check (real time: the indicator used to die after 300 ms), not a wait for something to happen.
        await Task.Delay(700);
        await bert.Until(otherElement => User(otherElement, annaId)?.GetProperty("isSpeakingViaLink").GetBoolean() == true);
        await anna.Post("linkptt", new { down = false });

        // bert is a guest without SpeakLinked: his link PTT stays in Raid and he gets a hint
        var bertId = SelfId(bertView);
        await bert.Post("tone", new { hz = 330 });
        await bert.Post("linkptt", new { down = true });
        var bertState = await bert.Until(otherElement => otherElement.GetProperty("audio").GetProperty("framesSent").GetInt64() > 10);
        Assert.Contains("Kein Recht", bertState.GetProperty("voiceHint").GetString());
        await AsyncWait.AssertStaysFalseAsync(async () => FramesFrom(await anna.State(), bertId) > 0, TimeSpan.FromMilliseconds(300), "without the right nothing goes over the link");
        Assert.Equal(0, FramesFrom(await anna.State(), bertId));
        await bert.Post("linkptt", new { down = false });

        // same channel: bert joins the lobby and anna hears his plain PTT
        await bert.Post("join", new { channel = "Lobby" });
        await bert.Until(otherElement => Channel(otherElement, "Lobby").GetProperty("isCurrent").GetBoolean());
        await bert.Post("ptt", new { down = true });
        await anna.Until(otherElement => FramesFrom(otherElement, bertId) > 10 && User(otherElement, bertId)?.GetProperty("isSpeaking").GetBoolean() == true);
        await bert.Until(otherElement => otherElement.GetProperty("transmitText").GetString() != ""); // so the empty text below is the release, not the start
        await bert.Post("ptt", new { down = false });

        // muted clients send nothing
        await bert.Post("mute", new { value = true });
        // The PTT release may still let frames out. Once the transmit text is gone, the capture thread has decided
        // "nothing" for a frame, and every frame before it is counted (one thread, in order): no wall-clock window.
        var settled = await bert.Until(otherElement => !otherElement.GetProperty("audio").GetProperty("ptt").GetBoolean() && otherElement.GetProperty("transmitText").GetString() == "");
        long sentBefore = settled.GetProperty("audio").GetProperty("framesSent").GetInt64();
        await bert.Post("ptt", new { down = true });
        await bert.Until(otherElement => otherElement.GetProperty("audio").GetProperty("ptt").GetBoolean()); // the key thread saw it, so the silence proves something
        await AsyncWait.AssertStaysFalseAsync(async () => (await bert.State()).GetProperty("audio").GetProperty("framesSent").GetInt64() != sentBefore,
            TimeSpan.FromMilliseconds(300), "a muted client sends nothing");
        Assert.Equal(sentBefore, (await bert.State()).GetProperty("audio").GetProperty("framesSent").GetInt64());
    }

    /// <summary>Package 29: every key action can be simulated through the API.</summary>
    [Fact]
    public async Task Keys_EveryAction_Simulated()
    {
        await anna.Post("connect", ConnectBody("anna"));
        await anna.Post("key", new { action = "PushToMute", down = true });
        await anna.Until(element => element.GetProperty("audio").GetProperty("pushToMute").GetBoolean());
        await anna.Post("key", new { action = "PushToMute", down = false });

        await anna.Post("key", new { action = "ToggleMute" });
        await anna.Until(element => element.GetProperty("server").GetProperty("selfMuted").GetBoolean());
        await anna.Post("key", new { action = "ToggleDeafen" });
        await anna.Until(element => element.GetProperty("server").GetProperty("selfDeafened").GetBoolean());

        var response = await anna.Http.PostAsJsonAsync("key", new { action = "Tanzen" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeafenedClient_HearsNothing_UntilUndeafened()
    {
        var annaId = SelfId(await anna.Post("connect", ConnectBody("anna")));
        await bert.Post("connect", ConnectBody("bert"));
        await bert.Post("deafen", new { value = true });
        await anna.Until(element => User(element, SelfId(element) + 1)?.GetProperty("statusText").GetString() == "(taub)");

        await anna.Post("tone", new { hz = 440 });
        await anna.Post("ptt", new { down = true });
        await anna.Until(element => element.GetProperty("audio").GetProperty("framesSent").GetInt64() > 10);
        var muted = await bert.State();
        Assert.Equal(0, FramesFrom(muted, annaId));
        Assert.Equal(-120, muted.GetProperty("audio").GetProperty("lastOutputLevelDb").GetDouble());

        await bert.Post("deafen", new { value = false });
        await bert.Until(element => FramesFrom(element, annaId) > 10 && element.GetProperty("audio").GetProperty("lastOutputLevelDb").GetDouble() > -60);
    }

    /// <summary>Package 47: the tones actually played show in /state.</summary>
    [Fact]
    public async Task State_SoundsPlayed_Listed()
    {
        await anna.Post("connect", ConnectBody("anna"));
        await bert.Post("connect", ConnectBody("bert"));
        static bool Played(JsonElement element, string sound) => element.GetProperty("sounds").EnumerateArray().Any(otherElement => otherElement.GetString() == sound);
        await anna.Until(element => Played(element, "Connected") && Played(element, "UserJoined"));
        await bert.Post("mute", new { value = true });
        await bert.Until(element => Played(element, "MicOff"));
    }

    [Fact]
    public async Task AdminActions_ThroughApi_Applied()
    {
        await anna.Post("connect", ConnectBody("anna"));
        await bert.Post("connect", ConnectBody("bert"));
        await anna.Post("redeem", new { token = server.State.PendingAdminToken });
        await anna.Until(element => element.GetProperty("server").GetProperty("isAdmin").GetBoolean());

        // raw protocol request: a new group
        await anna.Post("request", new { type = "createGroup", name = "Team", permissions = "Speak, SpeakLinked" });
        await anna.Until(element => element.GetProperty("server").GetProperty("groups").EnumerateArray().Any(otherElement => otherElement.GetProperty("name").GetString() == "Team"));

        // server mute shows up for everyone, kick disconnects
        await anna.Post("server-mute", new { user = "bert", value = true });
        await bert.Until(element => Channels(element).SelectMany(otherElement => otherElement.GetProperty("users").EnumerateArray())
            .Any(otherElement => otherElement.GetProperty("isSelf").GetBoolean() && otherElement.GetProperty("serverMuted").GetBoolean()));
        await anna.Post("kick", new { user = "bert", reason = "Test" });
        var kicked = await bert.Until(element => !element.GetProperty("connected").GetBoolean());
        Assert.Contains("gekickt", kicked.GetProperty("status").GetString());
    }
    [Fact]
    public async Task Chat_ThroughApi_RoundTrips()
    {
        await anna.Post("connect", ConnectBody("anna"));
        await bert.Post("connect", ConnectBody("bert"));
        await anna.Until(element => Channels(element).Sum(otherElement => otherElement.GetProperty("users").GetArrayLength()) == 2);

        static bool Has(JsonElement element, string from, string text) =>
            element.GetProperty("server").GetProperty("chat").EnumerateArray()
                .Any(otherElement => otherElement.GetProperty("fromNickname").GetString() == from && otherElement.GetProperty("text").GetString() == text);
        await anna.Post("chat", new { target = "channel", text = "hallo Lobby" });
        await bert.Until(element => Has(element, "anna", "hallo Lobby"));
        await bert.Post("chat", new { target = "private", to = "anna", text = "psst" });
        await anna.Until(element => Has(element, "bert", "psst"));
        await bert.Until(element => Has(element, "bert", "psst")); // the sender sees the own whisper
    }

    /// <summary>Package 102: someone folding away in the tree is gone for the debug API already.</summary>
    [Fact]
    public async Task State_LeavingUser_NotListed()
    {
        ServerViewModel fake = null!;
        await anna.OnUi(viewModel =>
        {
            fake = FakeServers.Admin(time: new ManualTimeProvider());
            fake.Post = action => action();
            fake.Leave.Delay = TimeSpan.FromMinutes(1);
            MainViewModelSetup.ShowServer(viewModel, fake);
            fake.Apply(new OVS.Shared.Protocol.Messages.Users.UserLeft(2));
        });
        var state = await anna.State();
        Assert.Null(User(state, 2));
        Assert.Single(fake.Tree.Channels.Single(channel => channel.Name == "Raid").Users); // still shown while it folds
    }

    /// <summary>
    /// Scripts and the tooling read /state by its field names: every name stays as it is when the view models behind it
    /// move. Arrays show as "[]"; the received frames are keyed by session id, so only the key itself is pinned.
    /// </summary>
    [Fact]
    public async Task State_Connected_KeepsEveryFieldName()
    {
        await anna.Post("connect", ConnectBody("anna"));
        await anna.Post("chat", new { target = "channel", text = "hallo" });
        var state = await anna.Until(element => element.GetProperty("server").GetProperty("chat").GetArrayLength() == 1);

        var names = new SortedSet<string>(StringComparer.Ordinal);
        FieldNames(state, "", names);

        string[] expected =
        [
            "audio",
            "audio.framesReceived",
            "audio.framesSent",
            "audio.lastOutputLevelDb",
            "audio.linkPtt",
            "audio.ptt",
            "audio.pushToMute",
            "audio.toneHz",
            "connected",
            "connection",
            "connection.bars",
            "connection.baselineMs",
            "connection.bitrate",
            "connection.buffer",
            "connection.concealed",
            "connection.downlink",
            "connection.jitter",
            "connection.rating",
            "connection.roundTrip",
            "connection.unreachable",
            "connection.uplink",
            "keyBindings",
            "keyBindings[].action",
            "keyBindings[].chord",
            "notices",
            "pingText",
            "server",
            "server.channels",
            "server.channels[].bitrate",
            "server.channels[].description",
            "server.channels[].id",
            "server.channels[].isCurrent",
            "server.channels[].isLinked",
            "server.channels[].isSeparator",
            "server.channels[].linkedNames",
            "server.channels[].name",
            "server.channels[].users",
            "server.channels[].users[].groupNames",
            "server.channels[].users[].isSelf",
            "server.channels[].users[].isSpeaking",
            "server.channels[].users[].isSpeakingViaLink",
            "server.channels[].users[].nickname",
            "server.channels[].users[].serverMuted",
            "server.channels[].users[].sessionId",
            "server.channels[].users[].statusText",
            "server.chat",
            "server.chat[].fromNickname",
            "server.chat[].target",
            "server.chat[].text",
            "server.chat[].toSessionId",
            "server.defaultChannelId",
            "server.groups",
            "server.groups[].assignableByMe",
            "server.groups[].id",
            "server.groups[].name",
            "server.groups[].permissions",
            "server.iconBytes",
            "server.iconHash",
            "server.isAdmin",
            "server.lastBanList",
            "server.lastUserList",
            "server.selfDeafened",
            "server.selfId",
            "server.selfMuted",
            "server.selfPermissions",
            "server.serverName",
            "server.welcomeText",
            "sounds",
            "status",
            "transmitText",
            "voiceHint",
        ];
        Assert.True(expected.SequenceEqual(names), "actual:" + Environment.NewLine + string.Join(Environment.NewLine, names.Select(name => $"            \"{name}\",")));
    }

    static void FieldNames(JsonElement element, string path, SortedSet<string> names)
    {
        if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) FieldNames(item, path + "[]", names);
        if (element.ValueKind != JsonValueKind.Object || path == "audio.framesReceived") return;
        foreach (var property in element.EnumerateObject())
        {
            var name = path.Length == 0 ? property.Name : $"{path}.{property.Name}";
            names.Add(name);
            FieldNames(property.Value, name, names);
        }
    }

    sealed class Instance : IAsyncDisposable
    {
        readonly TestDispatcher ui = new();
        readonly string dir = Directory.CreateTempSubdirectory("ovs-debug-").FullName;
        MainViewModel viewModel = null!;
        DebugApi api = null!;
        public HttpClient Http { get; private set; } = null!;
        public int Port { get; private set; }

        public static async Task<Instance> StartAsync()
        {
            var instance = new Instance();
            instance.viewModel = await instance.ui.InvokeAsync(() => Task.FromResult(MainViewModelSetup.Create(instance.dir, instance.ui.Post)));
            // Step 30: the port is free when probed, but another socket of the parallel run may take it before the debug
            // API binds it; then a new port is probed (at most 5 tries)
            for (int attempt = 1; ; attempt++)
            {
                var probe = new TcpListener(IPAddress.Loopback, 0);
                probe.Start();
                instance.Port = ((IPEndPoint)probe.LocalEndpoint).Port;
                probe.Stop();
                try
                {
                    // the debug API lists the system's devices while the view model runs without any, as with --no-audio
                    instance.api = new DebugApi(instance.viewModel, instance.viewModel.Keys, instance.viewModel.Audio, instance.Port, work => instance.ui.InvokeAsync(work), FakeAudioHardware.ListOneEach);
                    break;
                }
                catch (HttpListenerException) when (attempt < 5)
                {
                }
            }
            instance.Http = new HttpClient { BaseAddress = new Uri($"http://localhost:{instance.Port}/") };
            instance.Http.DefaultRequestHeaders.Add(DebugApi.Header, "1");
            return instance;
        }

        public async Task<JsonElement> Post(string path, object? body = null)
        {
            var response = await Http.PostAsJsonAsync(path, body ?? new { });
            var text = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, $"{path}: {text}");
            return JsonDocument.Parse(text).RootElement;
        }

        public Task OnUi(Action<MainViewModel> work) => ui.InvokeAsync<object?>(() =>
        {
            work(viewModel);
            return Task.FromResult<object?>(null);
        });

        public async Task<JsonElement> State() =>
            JsonDocument.Parse(await Http.GetStringAsync("state")).RootElement;

        public async Task<JsonElement> Until(Func<JsonElement, bool> condition, int timeoutMs = 15000) // returns once it holds
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            JsonElement state;
            do
            {
                state = await State();
                if (condition(state)) return state;
                await Task.Delay(50);
            } while (DateTime.UtcNow < deadline);
            Assert.Fail("Bedingung nicht erreicht. Zustand: " + state);
            return state;
        }

        public async ValueTask DisposeAsync()
        {
            api.Dispose();
            Http.Dispose();
            await ui.InvokeAsync<object?>(async () =>
            {
                await viewModel.DisposeAsync();
                return null;
            });
            ui.Dispose();
            Directory.Delete(dir, true);
        }
    }
}
