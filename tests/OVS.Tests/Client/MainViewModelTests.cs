using OVS.Client.Audio;
using OVS.Client.Input;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client;

/// <summary>Application shell against a real server, without audio devices.</summary>
public sealed class MainViewModelTests : IAsyncLifetime
{
    readonly TestDispatcher ui = new();
    readonly string dir = Directory.CreateTempSubdirectory("ovs-mvm-").FullName;
    TestServer server = null!;
    MainViewModel vm = null!;

    public async Task InitializeAsync()
    {
        server = await TestServer.StartAsync(d => d.Settings.WelcomeText = "Willkommen auf dem Testserver!");
        vm = await ui.InvokeAsync(() => Task.FromResult(new MainViewModel(dir, ui.Post, useAudioDevices: false)));
        vm.ConfirmTofu = _ => Task.FromResult(true);
    }

    public async Task DisposeAsync()
    {
        await ui.InvokeAsync<object?>(async () =>
        {
            await vm.DisposeAsync();
            return null;
        });
        ui.Dispose();
        await server.DisposeAsync();
        Directory.Delete(dir, true);
    }

    Task<T> OnUi<T>(Func<T> read) => ui.InvokeAsync(() => Task.FromResult(read()));

    Task ConnectAsync(bool saveBookmark) =>
        ui.InvokeAsync<object?>(async () =>
        {
            await vm.ConnectAsync(new ConnectChoice("127.0.0.1", server.Port, "anna", null, saveBookmark));
            return null;
        });

    [Fact]
    public async Task Connect_SavesBookmark_ToSettingsFile()
    {
        await ConnectAsync(saveBookmark: true);
        Assert.True(await OnUi(() => vm.IsConnected));

        var saved = ClientSettings.Load(dir, out _);
        var bookmark = Assert.Single(saved.Bookmarks);
        Assert.Equal(("127.0.0.1", server.Port, "anna"), (bookmark.Host, bookmark.Port, bookmark.Nickname));
    }

    [Fact]
    public async Task Connect_ShowsWelcomeText_AndPing()
    {
        await ConnectAsync(saveBookmark: false);
        Assert.Contains(await OnUi(() => vm.Notices.ToList()), n => n.Contains("Willkommen auf dem Testserver!"));

        string ping = "";
        for (int i = 0; i < 40 && ping.Length == 0; i++)
        {
            await Task.Delay(50);
            ping = await OnUi(() =>
            {
                vm.Tick();
                return vm.PingText;
            });
        }
        Assert.StartsWith("Ping ", ping);
    }

    [Fact]
    public void Deafened_Engine_IgnoresIncomingVoice()
    {
        using var keys = new KeyPoller();
        using var engine = new AudioEngine(keys, useDevices: false) { Deafened = true };
        engine.OnVoice(7, 0, 0, new VoiceEncoder().Encode(new float[AudioFormat.FrameSamples]));
        Assert.Empty(engine.FramesReceived);
        Assert.Equal(0, engine.Mixer.SpeakerCount);
    }

    [Fact]
    public async Task AudioDebugLog_RecordsKeysAndFrameRate()
    {
        using var keys = new KeyPoller();
        using var engine = new AudioEngine(keys, useDevices: false) { Connected = true, SelfMuted = false, Send = (_, _) => { } };
        var path = Path.Combine(dir, "audio-debug.log");
        using (new OVS.Client.Debug.AudioDebugLog(keys, engine, path))
        {
            engine.SetTone(440);
            keys.Simulate(ptt: true);
            await Task.Delay(2300);
            keys.Simulate(ptt: false);
            await Task.Delay(100);
        }
        var log = File.ReadAllText(path);
        Assert.Contains("PTT gedrückt", log);
        Assert.Contains("PTT losgelassen", log);
        // one full second of PTT at 20 ms per frame
        var rates = log.Split('\n').Where(l => l.Contains("Frames gesendet: "))
            .Select(l => int.Parse(l.Split("Frames gesendet: ")[1].Split('/')[0])).ToList();
        Assert.Contains(rates, r => r is >= 45 and <= 55);
    }

    [Fact]
    public async Task ToneInput_ReportsInputLevel()
    {
        using var keys = new KeyPoller();
        using var engine = new AudioEngine(keys, useDevices: false);
        var level = new TaskCompletionSource<float>();
        engine.InputLevel += db => level.TrySetResult(db);
        engine.SetTone(440);
        // 0.3 amplitude sine: RMS 0.212 = -13.5 dBFS
        Assert.InRange(await level.Task.WaitAsync(TimeSpan.FromSeconds(3)), -15f, -12f);
    }
}
