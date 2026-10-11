using Avalonia.Headless.XUnit;
using OVS.Client.Dialogs;
using OVS.Client.Input;
using OVS.Client.Models;
using OVS.Client.Services;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.Groups;
using OVS.Shared.Protocol.Messages.ServerInfo;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.ViewModels;

/// <summary>
/// A <see cref="PageFlowViewModel"/> without a main view model: it is built on delegates that record what the page flow asks of
/// its owner, in order (<see cref="Calls"/>). A test changes the owner side (<see cref="Settings"/>, <see cref="KnownDevices"/>,
/// <see cref="Dialogs"/>, <see cref="Threshold"/>, <see cref="ShownServer"/>) before it opens a page. What is posted waits in
/// <see cref="Posted"/> until <see cref="RunPosted"/>.
/// </summary>
sealed class PageFlowRig
{
    public PageFlowRig()
    {
        Dialogs = new DialogCallbacks { AskLeave = AskAsync, EditKeyBinding = Editor };
        Flow = new PageFlowViewModel(time: new ManualTimeProvider(), post: Posted.Enqueue, settings: () => Settings, knownDevices: () => KnownDevices,
            refreshDevices: () =>
            {
                Calls.Add("refreshDevices");
                PageAtRefresh = Flow!.Page;
            },
            captureChord: () => Task.FromResult(new KeyChord(1)),
            setKeyBindings: bindings =>
            {
                Calls.Add("setKeyBindings");
                AppliedKeyBindings.Add(bindings);
            },
            dialogs: () => Dialogs, checkNow: () => Task.FromResult(""), importSound: (_, _) => (null, null), previewSound: (_, _, _) => { },
            applyLive: live =>
            {
                Calls.Add("applyLive");
                AppliedLive.Add(live);
            },
            showAppearance: appearance =>
            {
                Calls.Add("showAppearance");
                ShownAppearances.Add(appearance);
            },
            setSelfTest: on =>
            {
                Calls.Add($"setSelfTest {on}");
                return Task.CompletedTask;
            },
            applySaved: saved =>
            {
                Calls.Add("applySaved");
                AppliedSaved.Add(saved);
                return Task.CompletedTask;
            },
            startLevelMeter: handler =>
            {
                Calls.Add("startLevelMeter");
                StartedHandler = handler;
            },
            stopLevelMeter: handler =>
            {
                Calls.Add("stopLevelMeter");
                StoppedHandler = handler;
            },
            readThresholdDb: () => Threshold, shownServer: () => ShownServer);
        Flow.PropertyChanged += (_, args) => Changes.Add(args.PropertyName!);
    }

    public PageFlowViewModel Flow { get; }

    public ClientSettings Settings { get; set; } = new() { VadThresholdDb = -30 };

    public DeviceLists? KnownDevices { get; set; }

    public DialogCallbacks Dialogs { get; set; }

    public float Threshold { get; set; } = -33f;

    public ServerViewModel? ShownServer { get; set; }

    /// <summary>The same delegate each time, so a test can tell it from another one.</summary>
    public Func<KeyBinding?, Func<KeyAction, Task<KeyChord?>>, Task<KeyBinding?>> Editor { get; } = (_, _) => Task.FromResult<KeyBinding?>(null);

    public Queue<LeaveChoice> Answers { get; } = new();

    public bool AskThrows { get; set; }

    public int Asked { get; private set; }

    public List<string> Calls { get; } = [];

    public List<string> Changes { get; } = [];

    public List<ClientSettings> AppliedLive { get; } = [];

    public List<IReadOnlyList<KeyBinding>> AppliedKeyBindings { get; } = [];

    public List<BackgroundAppearance> ShownAppearances { get; } = [];

    public List<ClientSettings> AppliedSaved { get; } = [];

    public List<Request> Sent { get; } = [];

    public Queue<Action> Posted { get; } = new();

    public Action<float>? StartedHandler { get; private set; }

    public Action<float>? StoppedHandler { get; private set; }

    public Page PageAtRefresh { get; private set; }

    /// <summary>Shows a server whose own user is an administrator; <paramref name="reply"/> answers a request at once.</summary>
    public ServerViewModel ShowAdminServer(Func<Request, Message?>? reply = null) =>
        ShownServer = FakeServers.Admin(Sent, new ManualTimeProvider(), Dialogs, reply);

    /// <summary>Both properties were raised, <paramref name="first"/> before <paramref name="second"/>.</summary>
    public void AssertChangedBefore(string first, string second)
    {
        int firstIndex = Changes.IndexOf(first), secondIndex = Changes.IndexOf(second);
        Assert.True(firstIndex >= 0, $"{first} was not raised");
        Assert.True(secondIndex >= 0, $"{second} was not raised");
        Assert.True(firstIndex < secondIndex, $"{first} came after {second}");
    }

    public void RunPosted()
    {
        while (Posted.TryDequeue(out var action)) action();
    }

    /// <summary>Opens the settings page and edits it, so it holds a change.</summary>
    public SettingsViewModel OpenEditedSettings()
    {
        Flow.OpenSettings();
        Flow.SettingsPage!.VadThresholdDb = -40;
        Calls.Clear();
        Changes.Clear();
        return Flow.SettingsPage;
    }

    /// <summary>Opens the administration and edits its server settings, so it holds a change.</summary>
    public async Task<AdminViewModel> OpenEditedAdminAsync()
    {
        await HangGuard.Bound(Flow.OpenAdminAsync());
        Flow.AdminPage!.ServerSettings.Name = "Neu";
        Changes.Clear();
        return Flow.AdminPage;
    }

    Task<LeaveChoice> AskAsync()
    {
        Asked++;
        return AskThrows ? throw new InvalidOperationException("Testmeldung") : Task.FromResult(Answers.Dequeue());
    }
}
