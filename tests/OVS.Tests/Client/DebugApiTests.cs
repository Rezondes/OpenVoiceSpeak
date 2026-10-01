using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using OVS.Client.Debug;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client;

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

    sealed class Instance : IAsyncDisposable
    {
        readonly TestDispatcher ui = new();
        readonly string dir = Directory.CreateTempSubdirectory("ovs-debug-").FullName;
        MainViewModel vm = null!;
        DebugApi api = null!;
        public HttpClient Http { get; private set; } = null!;
        public int Port { get; private set; }

        public static async Task<Instance> StartAsync()
        {
            var i = new Instance();
            i.vm = await i.ui.InvokeAsync(() => Task.FromResult(new MainViewModel(i.dir, i.ui.Post, useAudioDevices: false)));
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            i.Port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            i.api = new DebugApi(i.vm, i.Port, work => i.ui.InvokeAsync(work));
            i.Http = new HttpClient { BaseAddress = new Uri($"http://localhost:{i.Port}/") };
            i.Http.DefaultRequestHeaders.Add(DebugApi.Header, "1");
            return i;
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
            work(vm);
            return Task.FromResult<object?>(null);
        });

        public async Task<JsonElement> State() =>
            JsonDocument.Parse(await Http.GetStringAsync("state")).RootElement;

        public async Task<JsonElement> Until(Func<JsonElement, bool> condition, int timeoutMs = 5000)
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
                await vm.DisposeAsync();
                return null;
            });
            ui.Dispose();
            Directory.Delete(dir, true);
        }
    }

    static IEnumerable<JsonElement> Channels(JsonElement state) => state.GetProperty("server").GetProperty("channels").EnumerateArray();

    static JsonElement Channel(JsonElement state, string name) => Channels(state).Single(c => c.GetProperty("name").GetString() == name);

    static JsonElement? User(JsonElement state, uint id) =>
        Channels(state).SelectMany(c => c.GetProperty("users").EnumerateArray())
            .Cast<JsonElement?>().FirstOrDefault(u => u!.Value.GetProperty("sessionId").GetUInt32() == id);

    static long FramesFrom(JsonElement state, uint speaker) =>
        state.GetProperty("audio").GetProperty("framesReceived").TryGetProperty(speaker.ToString(), out var n) ? n.GetInt64() : 0;

    static uint SelfId(JsonElement state) => state.GetProperty("server").GetProperty("selfId").GetUInt32();

    object ConnectBody(string nickname) => new { host = "127.0.0.1", port = server.Port, nickname };

    [Fact]
    public async Task MissingHeader_Forbidden()
    {
        using var plain = new HttpClient();
        var response = await plain.GetAsync($"http://localhost:{anna.Port}/state");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Devices_AreListed()
    {
        var devices = JsonDocument.Parse(await anna.Http.GetStringAsync("devices")).RootElement;
        Assert.Equal(JsonValueKind.Array, devices.GetProperty("inputs").ValueKind);
        Assert.Equal(JsonValueKind.Array, devices.GetProperty("outputs").ValueKind);
    }

    [Fact]
    public async Task UnknownChannel_IsBadRequest()
    {
        await anna.Post("connect", ConnectBody("anna"));
        var response = await anna.Http.PostAsJsonAsync("join", new { channel = "gibtsnicht" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FullFlow_Linking_PttAndLinkPtt()
    {
        var a = await anna.Post("connect", ConnectBody("anna"));
        Assert.True(a.GetProperty("connected").GetBoolean(), a.ToString());
        var annaId = SelfId(a);
        await bert.Post("connect", ConnectBody("bert"));

        // anna becomes admin, creates Raid; bert moves there
        await anna.Post("redeem", new { token = server.State.PendingAdminToken });
        await anna.Until(s => s.GetProperty("server").GetProperty("isAdmin").GetBoolean());
        await anna.Post("create-channel", new { name = "Raid" });
        await anna.Until(s => Channels(s).Any(c => c.GetProperty("name").GetString() == "Raid"));
        await bert.Post("join", new { channel = "Raid" });
        await bert.Until(s => Channel(s, "Raid").GetProperty("isCurrent").GetBoolean());

        // Normal PTT from the lobby does not reach Raid
        await anna.Post("tone", new { hz = 440 });
        await anna.Post("ptt", new { down = true });
        await anna.Until(s => s.GetProperty("audio").GetProperty("framesSent").GetInt64() > 10);
        await Task.Delay(300);
        Assert.Equal(0, FramesFrom(await bert.State(), annaId));
        await anna.Post("ptt", new { down = false });

        // Link Lobby and Raid: normal PTT still stays in the lobby ...
        await anna.Post("link", new { a = "Lobby", b = "Raid" });
        await bert.Until(s => Channel(s, "Raid").GetProperty("isLinked").GetBoolean());
        await anna.Post("ptt", new { down = true });
        await Task.Delay(500);
        Assert.Equal(0, FramesFrom(await bert.State(), annaId));
        await anna.Post("ptt", new { down = false });

        // ... but link PTT reaches Raid, marked as link transmission
        await anna.Post("linkptt", new { down = true });
        var b = await bert.Until(s => FramesFrom(s, annaId) > 10 && User(s, annaId)?.GetProperty("isSpeakingViaLink").GetBoolean() == true);
        Assert.Contains("Links", (await anna.State()).GetProperty("transmitText").GetString());
        // The indicator must stay on while anna keeps talking (it used to go dark for good after 300 ms).
        // Polled, because a starved test machine may drop it for a moment.
        await Task.Delay(700);
        await bert.Until(s => User(s, annaId)?.GetProperty("isSpeakingViaLink").GetBoolean() == true, 1000);
        await anna.Post("linkptt", new { down = false });

        // bert is a guest without SpeakLinked: his link PTT stays in Raid and he gets a hint
        var bertId = SelfId(b);
        await bert.Post("tone", new { hz = 330 });
        await bert.Post("linkptt", new { down = true });
        var bertState = await bert.Until(s => s.GetProperty("audio").GetProperty("framesSent").GetInt64() > 10);
        Assert.Contains("Kein Recht", bertState.GetProperty("voiceHint").GetString());
        await Task.Delay(300);
        Assert.Equal(0, FramesFrom(await anna.State(), bertId));
        await bert.Post("linkptt", new { down = false });

        // same channel: bert joins the lobby and anna hears his plain PTT
        await bert.Post("join", new { channel = "Lobby" });
        await bert.Until(s => Channel(s, "Lobby").GetProperty("isCurrent").GetBoolean());
        await bert.Post("ptt", new { down = true });
        await anna.Until(s => FramesFrom(s, bertId) > 10 && User(s, bertId)?.GetProperty("isSpeaking").GetBoolean() == true);
        await bert.Post("ptt", new { down = false });

        // muted clients send nothing
        await bert.Post("mute", new { value = true });
        // The PTT release above may still let one 20 ms frame out: measure once the counter stands still.
        long sentBefore = -1, now = (await bert.State()).GetProperty("audio").GetProperty("framesSent").GetInt64();
        for (int i = 0; i < 20 && now != sentBefore; i++)
        {
            sentBefore = now;
            await Task.Delay(60);
            now = (await bert.State()).GetProperty("audio").GetProperty("framesSent").GetInt64();
        }
        await bert.Post("ptt", new { down = true });
        await Task.Delay(300);
        Assert.Equal(sentBefore, (await bert.State()).GetProperty("audio").GetProperty("framesSent").GetInt64());
    }

    /// <summary>Package 29: every key action can be simulated through the API.</summary>
    [Fact]
    public async Task Keys_SimulateEveryAction()
    {
        await anna.Post("connect", ConnectBody("anna"));
        await anna.Post("key", new { action = "PushToMute", down = true });
        await anna.Until(s => s.GetProperty("audio").GetProperty("pushToMute").GetBoolean());
        await anna.Post("key", new { action = "PushToMute", down = false });

        await anna.Post("key", new { action = "ToggleMute" });
        await anna.Until(s => s.GetProperty("server").GetProperty("selfMuted").GetBoolean());
        await anna.Post("key", new { action = "ToggleDeafen" });
        await anna.Until(s => s.GetProperty("server").GetProperty("selfDeafened").GetBoolean());

        var response = await anna.Http.PostAsJsonAsync("key", new { action = "Tanzen" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeafenedClient_HearsNothing_UntilUndeafened()
    {
        var annaId = SelfId(await anna.Post("connect", ConnectBody("anna")));
        await bert.Post("connect", ConnectBody("bert"));
        await bert.Post("deafen", new { value = true });
        await anna.Until(s => User(s, SelfId(s) + 1)?.GetProperty("statusText").GetString() == "(taub)");

        await anna.Post("tone", new { hz = 440 });
        await anna.Post("ptt", new { down = true });
        await anna.Until(s => s.GetProperty("audio").GetProperty("framesSent").GetInt64() > 10);
        var muted = await bert.State();
        Assert.Equal(0, FramesFrom(muted, annaId));
        Assert.Equal(-120, muted.GetProperty("audio").GetProperty("lastOutputLevelDb").GetDouble());

        await bert.Post("deafen", new { value = false });
        await bert.Until(s => FramesFrom(s, annaId) > 10 && s.GetProperty("audio").GetProperty("lastOutputLevelDb").GetDouble() > -60);
    }

    /// <summary>Package 47: the tones actually played show in /state.</summary>
    [Fact]
    public async Task Sounds_ListedInState()
    {
        await anna.Post("connect", ConnectBody("anna"));
        await bert.Post("connect", ConnectBody("bert"));
        static bool Played(JsonElement s, string sound) => s.GetProperty("sounds").EnumerateArray().Any(e => e.GetString() == sound);
        await anna.Until(s => Played(s, "Connected") && Played(s, "UserJoined"));
        await bert.Post("mute", new { value = true });
        await bert.Until(s => Played(s, "MicOff"));
    }

    [Fact]
    public async Task AdminActions_ThroughApi()
    {
        await anna.Post("connect", ConnectBody("anna"));
        await bert.Post("connect", ConnectBody("bert"));
        await anna.Post("redeem", new { token = server.State.PendingAdminToken });
        await anna.Until(s => s.GetProperty("server").GetProperty("isAdmin").GetBoolean());

        // raw protocol request: a new group
        await anna.Post("request", new { type = "createGroup", name = "Team", permissions = "Speak, SpeakLinked" });
        await anna.Until(s => s.GetProperty("server").GetProperty("groups").EnumerateArray().Any(g => g.GetProperty("name").GetString() == "Team"));

        // server mute shows up for everyone, kick disconnects
        await anna.Post("server-mute", new { user = "bert", value = true });
        await bert.Until(s => Channels(s).SelectMany(c => c.GetProperty("users").EnumerateArray())
            .Any(u => u.GetProperty("isSelf").GetBoolean() && u.GetProperty("serverMuted").GetBoolean()));
        await anna.Post("kick", new { user = "bert", reason = "Test" });
        var kicked = await bert.Until(s => !s.GetProperty("connected").GetBoolean());
        Assert.Contains("gekickt", kicked.GetProperty("status").GetString());
    }
    [Fact]
    public async Task Chat_RoundTrip()
    {
        await anna.Post("connect", ConnectBody("anna"));
        await bert.Post("connect", ConnectBody("bert"));
        await anna.Until(s => Channels(s).Sum(c => c.GetProperty("users").GetArrayLength()) == 2);

        static bool Has(JsonElement s, string from, string text) =>
            s.GetProperty("server").GetProperty("chat").EnumerateArray()
                .Any(c => c.GetProperty("fromNickname").GetString() == from && c.GetProperty("text").GetString() == text);
        await anna.Post("chat", new { target = "channel", text = "hallo Lobby" });
        await bert.Until(s => Has(s, "anna", "hallo Lobby"));
        await bert.Post("chat", new { target = "private", to = "anna", text = "psst" });
        await anna.Until(s => Has(s, "bert", "psst"));
        await bert.Until(s => Has(s, "bert", "psst")); // the sender sees the own whisper
    }

    /// <summary>Package 102: someone folding away in the tree is gone for the debug API already.</summary>
    [Fact]
    public async Task LeavingUser_NotInState()
    {
        ServerViewModel fake = null!;
        await anna.OnUi(vm =>
        {
            fake = FakeServers.Admin(time: new ManualTimeProvider());
            fake.Post = a => a();
            fake.Leave.Delay = TimeSpan.FromMinutes(1);
            vm.Server = fake;
            fake.Apply(new OVS.Shared.Protocol.UserLeft(2));
        });
        var state = await anna.State();
        Assert.Null(User(state, 2));
        Assert.Single(fake.Channels.Single(c => c.Name == "Raid").Users); // still shown while it folds
    }
}
