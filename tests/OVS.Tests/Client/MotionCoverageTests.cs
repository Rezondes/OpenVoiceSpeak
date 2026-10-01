using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OVS.Client.Localization;
using OVS.Client.Net;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Client.Views;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client;

/// <summary>
/// Package 109: one walk through every user action of the client, in both displays. In the animated display each
/// action starts at least one animation (counted by <see cref="Motion.Started"/>; for effects that only a style plays,
/// a hover or a check mark, the target is seen passing through in-between values), in the simplified one none, and
/// both end in the same state.
/// <para>
/// What the walk cannot drive headless, and why (AC1):
/// <list type="bullet">
/// <item>Picking a file (server logo, backup upload, own sounds): an OS file picker. What follows the pick is in the
/// walk ("upload a logo" from <see cref="AdminViewModel.UploadIconAsync"/>).</item>
/// <item>Downloading a backup or a log: an OS save dialog that writes a file (the warning before it is in the walk).</item>
/// <item>What brings up the server password, certificate, update offer and channel password dialogs: a real connection,
/// an update check, a locked channel the admin of the fake server bypasses. The walk opens these dialogs directly.</item>
/// <item>Restoring a backup to the end: the server restarts and drops the connection. The walk asks the restore
/// question and cancels it; the dropped connection is "disconnect".</item>
/// <item>Dragging a channel or a group (Package 96 keeps its own behaviour, A114): the drop's result, the new order,
/// is "move a channel" and "reorder groups".</item>
/// <item>Speaking, push-to-talk and the level meter: audio devices (covered by <see cref="LiveMotionTests"/>).</item>
/// <item>The window's own buttons (minimise, maximise, close): the OS window.</item>
/// </list>
/// </para>
/// </summary>
public sealed class MotionCoverageTests : IDisposable
{
    public void Dispose() => Motion.IsAnimated = true;

    static void Frame()
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Renders frames for that long and calls <paramref name="look"/> after each one (the clock runs on real time).</summary>
    static void Watch(int milliseconds, Action? look = null)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < milliseconds)
        {
            Frame();
            look?.Invoke();
            Thread.Sleep(1);
        }
    }

    // ---- the walk ----

    sealed class Walk(MainWindow main, MainViewModel vm)
    {
        public MainWindow Main { get; } = main;
        public MainViewModel Vm { get; } = vm;
        public ServerViewModel Server { get; set; } = null!;
        public List<Request> Sent { get; } = [];
        public AdminViewModel Admin => Vm.AdminPage!;
        /// <summary>Animations still running from before the walk (other tests' windows): the walk waits for its own only.</summary>
        public int Running { get; } = Motion.Running;
    }

    /// <param name="Look">For effects only a style plays: the visual whose transitions are watched.</param>
    /// <param name="Prepare">Runs first and is not counted (scrolling a control into view).</param>
    /// <param name="Undo">Runs after the watch, before the end state is taken (a toggle set back).</param>
    sealed record Step(string Name, Action<Walk> Act, Func<Walk, Visual?>? Look = null, Action<Walk>? Prepare = null, Action<Walk>? Undo = null);

    /// <param name="Movers">What passed through in-between looks (for the messages).</param>
    sealed record Result(string Name, int Started, bool Moved, string State, string Movers = "");

    const string Idle = "nothing (idle)";

    /// <summary>The walks of both displays, made once for the tests that need them.</summary>
    static readonly Dictionary<DisplayMode, List<Result>> walks = [];

    static List<Result> WalkThrough(DisplayMode display)
    {
        if (walks.TryGetValue(display, out var done)) return done;
        string dir = Directory.CreateTempSubdirectory("ovs-coverage-").FullName;
        new ClientSettings { Display = display }.Save(dir);
        var vm = new MainViewModel(dir, a => Dispatcher.UIThread.Post(a), useAudioDevices: false);
        var main = new MainWindow { DataContext = vm, Width = 1100, Height = 700 };
        vm.Dialogs = SimpleDialogs.For(main.Overlay, () => vm.Server?.Mirror.Groups);
        main.Show();
        Watch(200);
        Motion.Apply(main, display);
        var walk = new Walk(main, vm);
        var results = new List<Result>();
        foreach (var step in Steps(walk)) results.Add(Run(walk, step));
        main.Close();
        Motion.IsAnimated = true;
        try
        {
            Directory.Delete(dir, true);
        }
        catch (IOException)
        {
        }
        return walks[display] = results;
    }

    static Result Run(Walk w, Step step)
    {
        if (step.Prepare is { } prepare)
        {
            prepare(w);
            Settle(w, watchLeast: false);
        }
        var target = step.Look?.Invoke(w);
        var seen = new Dictionary<Visual, HashSet<string>>();
        void Look()
        {
            if (target is null) return;
            foreach (var visual in target.GetSelfAndVisualDescendants())
            {
                if (!seen.TryGetValue(visual, out var looks)) seen[visual] = looks = [];
                looks.Add(Looks(visual));
            }
        }
        Motion.ResetStarted();
        Look();
        step.Act(w);
        Settle(w, Look);
        if (step.Undo is { } undo)
        {
            undo(w);
            Settle(w, watchLeast: false);
        }
        // a style transition passes through values between its start and its end; a jump has two. Frames may come late
        // and miss a 120 ms transition's middle: a look that changed counts as well when the change ran through a
        // transition of the animated display (Motion.Fast or Motion.Normal long; Fluent's own are shorter, the
        // simplified display has none of these)
        var movers = seen.Where(p => p.Value.Count >= 3 || p.Value.Count == 2 && HasMotionTransition(p.Key)).Select(p => $"{p.Key.GetType().Name} {string.Join(".", (p.Key as StyledElement)?.Classes ?? [])} {(p.Key as StyledElement)?.Name}").ToList();
        return new Result(step.Name, Motion.Started, movers.Count > 0, State(w), string.Join("; ", movers.Take(5)));
    }

    /// <summary>
    /// Frames until the action is over: no animation started from code runs any more, no list row enters or leaves, and
    /// the end state has held for a moment. The animated display watches an action at least 160 ms (a waiting state
    /// shows after 150 ms, a style transition takes 120 to 220 ms); the simplified one changes at once and gets a few frames.
    /// </summary>
    /// <param name="watchLeast">False for what only prepares or sets back: no waiting state or transition to see there.</param>
    static void Settle(Walk w, Action? look = null, bool watchLeast = true)
    {
        bool animated = Motion.IsAnimated;
        int least = !watchLeast ? 0 : animated ? 160 : 20, hold = animated ? 25 : 10;
        var watch = Stopwatch.StartNew();
        string? last = null;
        long since = 0;
        while (watch.ElapsedMilliseconds < 2000)
        {
            Frame();
            look?.Invoke();
            Thread.Sleep(1);
            if (Motion.Running > w.Running || Rows(w).Any(r => r.Classes.Contains("entering") || r.Classes.Contains("leaving")))
            {
                last = null;
                continue;
            }
            var now = State(w);
            if (now != last)
            {
                last = now;
                since = watch.ElapsedMilliseconds;
            }
            else if (watch.ElapsedMilliseconds >= least && watch.ElapsedMilliseconds - since >= hold) return;
        }
    }

    static List<Control> Rows(Walk w) =>
        w.Main.GetVisualDescendants().OfType<ItemsControl>().Where(ItemMotion.GetEnter).SelectMany(l => l.GetRealizedContainers()).ToList();

    /// <summary>The mouse leaves the window: nothing is hovered, no tooltip waits to open.</summary>
    static void Away(Walk w) => w.Main.MouseMove(new Point(-100, -100));

    static bool HasMotionTransition(Visual visual) =>
        visual.Transitions?.Any(t => t is Avalonia.Animation.TransitionBase { Duration: var d } && (d == Motion.Fast || d == Motion.Normal)) == true;

    static string Brush(IBrush? brush) => (brush as ISolidColorBrush)?.Color.ToString() ?? brush?.GetType().Name ?? "";

    static string Looks(Visual v) => $"{v.RenderTransform?.Value}|{v.Opacity:F3}|" + v switch
    {
        Border b => $"{b.BoxShadow}|{Brush(b.Background)}|{Brush(b.BorderBrush)}",
        ContentPresenter p => $"{p.BoxShadow}|{Brush(p.Background)}|{Brush(p.BorderBrush)}",
        _ => "",
    };

    /// <summary>What counts after an action: the models, and that nothing of a movement is left on the screen.</summary>
    static string State(Walk w)
    {
        var vm = w.Vm;
        var main = w.Main;
        var text = new StringBuilder();
        text.AppendLine($"page={vm.Page} connected={vm.IsConnected} connecting={vm.IsConnecting} status={vm.Status}");
        var card = (Border)main.Overlay.Children[1];
        text.AppendLine($"dialog={main.Overlay.IsOpen}/{main.Overlay.IsVisible} card={card.Opacity:F2}/{card.RenderTransform?.Value.M11 ?? 1:F2} drawer={main.Classes.Contains("drawer")}");
        text.AppendLine($"bookmarks={string.Join(",", vm.Bookmarks.Select(b => b.Name))}");
        if (vm.Server is { } server)
        {
            foreach (var channel in server.Channels)
                text.AppendLine($"{channel.Name}{(channel.IsLinked ? " linked" : "")}{(channel.IsCurrent ? " current" : "")} {channel.SlotText}: "
                    + string.Join(",", channel.Users.Select(u => u.Nickname + (u.ServerMuted ? " muted" : ""))));
            text.AppendLine($"self muted={server.SelfMuted} deafened={server.SelfDeafened}");
        }
        if (vm.Chat is { } chat) text.AppendLine($"chat tab={chat.Tabs.IndexOf(chat.Selected)} lines={chat.Selected.Entries.Count}");
        if (vm.SettingsPage is { } page)
        {
            var settings = page.ToSettings(vm.Settings);
            settings.Display = DisplayMode.Animated; // the one setting the two walks differ in
            text.AppendLine(JsonSerializer.Serialize(settings));
        }
        if (vm.AdminPage is { } admin)
        {
            text.AppendLine($"admin tab={AdminTabs(w).SelectedIndex} groups={string.Join(",", admin.Groups.Select(g => $"{g.Name} {(int)g.Permissions}"))} selected={admin.SelectedGroup?.Name} "
                + $"users={string.Join(",", admin.Users.Select(u => u.Nickname + (u.IsServerMuted ? " muted" : "")))} bans={admin.Bans.Count} backups={admin.Backups.Count} logo={admin.HasIcon}");
            text.AppendLine($"links pending={admin.Links.PendingText} selected={string.Join(",", admin.Links.Rows.Where(r => r.IsSelected).Select(r => r.Name))} "
                + $"server settings={admin.RemovePassword}/{admin.LogRotateDaily}/{admin.AutoRestart}");
            text.AppendLine($"logs hits={admin.Logs.ShowHits}/{admin.Logs.Hits.Count} open={admin.Logs.IsFileOpen} page={admin.Logs.Page} lines={admin.Logs.Lines.Count}");
        }
        // nothing of a movement is left: no picture, ghost or popup in the overlay, every list entry whole
        var rows = Rows(w);
        text.AppendLine($"overlay={OverlayLayer.GetOverlayLayer(main)?.Children.Count} pictures={main.FindControl<Border>("PageGhost")!.IsVisible}/{main.FindControl<Border>("TreeGhost")!.IsVisible} "
            + $"rows={rows.Count} unsettled={rows.Count(r => r.Opacity < 1 || r.Classes.Contains("entering") || r.Classes.Contains("leaving"))}");
        return text.ToString();
    }

    // ---- what the walk finds on the screen ----

    static readonly Guid Archive = Guid.NewGuid(), Moderators = Guid.NewGuid(), AnnasBan = Guid.NewGuid();

    static UserInfo Me(Guid channel) => new(1, "fp1", "ich", channel, false, false, false, Permission.All, [WellKnownGroups.Admin]);

    static UserInfo Bert(Guid channel, bool serverMuted = false) =>
        new(3, "fp3", "bert", channel, false, false, serverMuted, Permission.None, [WellKnownGroups.Guest], CanBeModeratedByMe: true);

    static GroupInfo Guest => new(WellKnownGroups.Guest, "Gast", Permission.Speak, true);
    static GroupInfo AdminGroup => new(WellKnownGroups.Admin, "Admin", Permission.All, true);
    static GroupInfo Moderator => new(Moderators, "Moderator", Permission.Speak | Permission.UserKick, true);

    static ChannelViewModel Channel(Walk w, string name) => w.Server.Channels.Single(c => c.Name == name);
    static UserViewModel User(Walk w, string nickname) => w.Server.Channels.SelectMany(c => c.Users).Single(u => u.Nickname == nickname);

    static Border Row(Walk w, Func<object?, bool> of) =>
        w.Main.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("row") && b.ContextMenu is not null && of(b.DataContext));

    static Button BookmarkButton(Walk w) => w.Main.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("bookmark") && b.IsEffectivelyVisible);

    static AdminView AdminPage(Walk w) => w.Main.GetVisualDescendants().OfType<AdminView>().Single();
    static TabControl AdminTabs(Walk w) => AdminPage(w).FindControl<TabControl>("Tabs")!;
    static T Named<T>(Walk w, string name) where T : Control => AdminPage(w).FindControl<T>(name)!;

    /// <summary>A shown control of the administration (the selected tab's content).</summary>
    static T Shown<T>(Walk w, Func<T, bool> which) where T : Control =>
        AdminPage(w).GetVisualDescendants().OfType<T>().First(c => c.IsEffectivelyVisible && c.IsEffectivelyEnabled && which(c));

    static Button CommandButton(Walk w, System.Windows.Input.ICommand command) => Shown<Button>(w, b => b.Command == command);

    static KnownUserViewModel Anna(Walk w) => w.Admin.Users.Single(u => u.Nickname == "anna");

    static string LastUserList(Walk w) => w.Sent.OfType<ListUsers>().Last().RequestId!;

    static KnownUserInfo KnownMe => new("fp1", "ich", [WellKnownGroups.Admin]);
    static KnownUserInfo KnownAnna(bool muted) => new("fp2", "anna", [WellKnownGroups.Guest], ServerMuted: muted, CanBeModeratedByMe: true);
    static LogFileInfo ServerLog => new("server-1", LogKind.Server, null, null, DateTimeOffset.Now.AddHours(-1), DateTimeOffset.Now, 100);

    static Action<Walk> Show(Func<Walk, Control> control) => x =>
    {
        Away(x); // the mouse comes to it afresh, not by scrolling it under the pointer
        control(x).BringIntoView();
    };

    /// <summary>A drop-down opens and closes; the popup fades and slides (counted), its picture fades when it closes.</summary>
    static Step Drop(string name, Func<Walk, ComboBox> combo) =>
        new($"open and close {name}", x => combo(x).IsDropDownOpen = true, null, Show(combo), x => combo(x).IsDropDownOpen = false);

    /// <summary>A check box toggles (its mark pops or shrinks, a style), set back afterwards when <paramref name="back"/>.</summary>
    static Step Toggle(string name, Func<Walk, ToggleButton> box, bool back = false)
    {
        ToggleButton? shown = null; // the one toggled, also for the undo
        return new(name, x => shown!.IsChecked = !shown.IsChecked, x => shown = box(x), Show(box),
            back ? x => shown!.IsChecked = !shown.IsChecked : null);
    }

    /// <summary>
    /// The mouse clicks a button: its face lifts under the mouse and gives way when pressed (styles of the animated display;
    /// Fluent's own press tilt of the button itself is not watched, it belongs to the simplified look too).
    /// </summary>
    static Step Click(string name, Func<Walk, Button> button, Action<Walk>? then = null, Action<Walk>? prepare = null) =>
        new(name, x =>
        {
            var at = Center(button(x), x.Main);
            x.Main.MouseMove(at);
            x.Main.MouseDown(at, MouseButton.Left);
            x.Main.MouseUp(at, MouseButton.Left);
            then?.Invoke(x);
        }, x => button(x).Presenter, x =>
        {
            prepare?.Invoke(x);
            Show(button)(x);
        }, Away);

    static Point Center(Control control, Visual root) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root)!.Value;

    static IEnumerable<Step> Menu(string name, Func<Walk, Control> owner)
    {
        yield return new($"open {name}", w => owner(w).ContextMenu!.Open(owner(w)));
        yield return new($"close {name}", w => owner(w).ContextMenu!.Close());
    }

    /// <summary>A dialog opens and is closed again without an answer (cancelled).</summary>
    static IEnumerable<Step> Dialog(string name, Action<Walk> open)
    {
        yield return new($"open the {name} dialog", open);
        yield return new($"close the {name} dialog", w => w.Main.Overlay.Close());
    }

    /// <summary>
    /// Every settings control once: check boxes and radio buttons toggle (and are set back), drop-downs open and close,
    /// sliders and buttons are hovered. Rows of a list (the sounds) count once per kind.
    /// </summary>
    static IEnumerable<Step> SettingsControls(Walk w)
    {
        var view = w.Main.GetVisualDescendants().OfType<SettingsView>().Single();
        var all = view.GetVisualDescendants().OfType<Control>()
            .Where(c => c is CheckBox or RadioButton or ComboBox or Slider && c.IsEffectivelyVisible && c.IsEffectivelyEnabled).ToList();
        var controls = all.Where(c => c.DataContext is SettingsViewModel)
            .Concat(all.Where(c => c.DataContext is not SettingsViewModel).GroupBy(c => (c.GetType(), c.DataContext?.GetType())).Select(g => g.First()))
            .ToList();
        int index = 0;
        foreach (var control in controls)
        {
            string name = $"settings: {control.GetType().Name} {++index} ({AutomationProperties.GetName(control) ?? (control as ContentControl)?.Content as string})";
            var show = Show(_ => control);
            switch (control)
            {
                case RadioButton radio when radio.IsChecked == true:
                    continue; // chosen back by the other one's undo
                case RadioButton radio:
                    var before = view.GetVisualDescendants().OfType<RadioButton>().Single(r => r.IsChecked == true);
                    yield return new(name, _ => radio.IsChecked = true, _ => radio, show, _ => before.IsChecked = true);
                    break;
                case CheckBox check:
                    yield return new(name, _ => check.IsChecked = !check.IsChecked, _ => check, show, _ => check.IsChecked = !check.IsChecked);
                    break;
                case ComboBox combo:
                    yield return new(name, _ => combo.IsDropDownOpen = true, null, show, _ => combo.IsDropDownOpen = false);
                    break;
                default: // a slider: its thumb grows under the mouse
                    yield return new(name, x => x.Main.MouseMove(Center(control, x.Main)), _ => control, show, Away);
                    break;
            }
        }
        var cancel = view.GetVisualDescendants().OfType<Button>().First(b => b.Content is string text && text == Strings.Dlg_Cancel);
        yield return new("settings: hover a button", x => x.Main.MouseMove(Center(cancel, x.Main)), _ => cancel, Show(_ => cancel), Away);
    }

    /// <summary>AC1: every user action, in the order a user could do them.</summary>
    static IEnumerable<Step> Steps(Walk w)
    {
        // ---- home ----
        foreach (var s in Dialog("connect", x => _ = SimpleDialogs.Connect(x.Main.Overlay, x.Vm.Settings))) yield return s;
        foreach (var s in Dialog("server password", x => _ = SimpleDialogs.AskPassword(x.Main.Overlay, "Gilde"))) yield return s;
        foreach (var s in Dialog("certificate", x => _ = SimpleDialogs.Tofu(x.Main.Overlay, new TofuPrompt("gilde.example.org", 7000, "AB:CD:EF:01", TofuResult.Unknown))))
            yield return s;
        foreach (var s in Dialog("update offer", x => _ = SimpleDialogs.OfferUpdate(x.Main.Overlay,
            new UpdateOffer("9.9.9", "v9.9.9", "Neu", DateTimeOffset.Now, new Uri("https://example.org/ovs.exe"), new Uri("https://example.org/ovs.sha256")))))
            yield return s;
        yield return new("connect, and it fails", x =>
        {
            x.Vm.IsConnecting = true;
            Watch(150);
            x.Vm.Status = "Verbindung fehlgeschlagen";
            x.Vm.IsConnecting = false;
        });
        yield return new("add a bookmark", x =>
        {
            var settings = x.Vm.Settings;
            settings.Bookmarks.Add(new Bookmark("Gilde", "gilde.example.org", 7000, "ich"));
            x.Vm.ApplySettings(settings);
        });
        foreach (var s in Menu("a bookmark's menu", BookmarkButton)) yield return s;
        foreach (var s in Dialog("edit bookmark", x => _ = x.Vm.EditBookmarkAsync(x.Vm.Settings.Bookmarks[0]))) yield return s;
        foreach (var s in Dialog("delete bookmark", x => _ = x.Vm.DeleteBookmarkAsync(x.Vm.Settings.Bookmarks[0]))) yield return s;
        yield return new("remove a bookmark", x =>
        {
            var settings = x.Vm.Settings;
            settings.Bookmarks.Clear();
            x.Vm.ApplySettings(settings);
        });

        // ---- settings ----
        yield return new("open the settings", x => x.Vm.OpenSettings());
        foreach (var s in SettingsControls(w)) yield return s;
        foreach (var s in Dialog("key binding", x => x.Vm.SettingsPage!.AddKeyBindingCommand.Execute(null))) yield return s;
        yield return new("close the settings", x => x.Vm.ClosePage());

        // ---- connected: channels ----
        yield return new("connect", x =>
        {
            x.Server = FakeServers.Admin(x.Sent, dialogs: x.Vm.Dialogs);
            x.Server.Post = a => Dispatcher.UIThread.Post(a); // as the main view model does: leaving rows go on the UI thread
            x.Vm.Server = x.Server;
        });
        foreach (var s in Menu("a channel's menu", x => Row(x, d => d is ChannelViewModel { Name: "Lobby" }))) yield return s;
        yield return new("join a channel", x => x.Server.Apply(new UserUpdated(Me(FakeServers.Raid))));
        yield return new("switch the channel", x => x.Server.Apply(new UserUpdated(Me(FakeServers.Lobby))));
        foreach (var s in Dialog("create channel", x => _ = Channel(x, "Lobby").CreateCommand.ExecuteAsync(null))) yield return s;
        yield return new("create a channel", x => x.Server.Apply(new ChannelAdded(new ChannelInfo(Archive, "Archiv", "", 2))));
        foreach (var s in Dialog("edit channel", x => _ = Channel(x, "Archiv").EditCommand.ExecuteAsync(null))) yield return s;
        yield return new("edit a channel: rename it", x => x.Server.Apply(new ChannelUpdated(new ChannelInfo(Archive, "Altes Archiv", "Alte Sachen", 2))));
        yield return new("edit a channel: mute it", x => x.Server.Apply(new ChannelUpdated(new ChannelInfo(Archive, "Altes Archiv", "Alte Sachen", 2, IsMuted: true))));
        yield return new("edit a channel: limit its users", x => x.Server.Apply(new ChannelUpdated(new ChannelInfo(Archive, "Altes Archiv", "Alte Sachen", 2, true, MaxUsers: 5))));
        yield return new("move a channel", x => x.Server.Apply(new ChannelUpdated(new ChannelInfo(Archive, "Altes Archiv", "Alte Sachen", -1, true, MaxUsers: 5))));
        foreach (var s in Dialog("link channels", x => _ = Channel(x, "Altes Archiv").LinkCommand.ExecuteAsync(null))) yield return s;
        yield return new("link channels", x => x.Server.Apply(new ChannelsLinked(Archive, FakeServers.Lobby)));
        yield return new("unlink channels", x => x.Server.Apply(new ChannelsUnlinked(Archive, FakeServers.Lobby)));
        foreach (var s in Dialog("channel password", x => _ = SimpleDialogs.AskChannelPassword(x.Main.Overlay, "Raid"))) yield return s;
        foreach (var s in Dialog("delete channel", x => _ = Channel(x, "Altes Archiv").DeleteCommand.ExecuteAsync(null))) yield return s;
        yield return new("delete a channel", x => x.Server.Apply(new ChannelRemoved(Archive)));

        // ---- connected: users ----
        yield return new("someone joins", x => x.Server.Apply(new UserJoined(Bert(FakeServers.Lobby))));
        foreach (var s in Menu("a user's menu", x => Row(x, d => d is UserViewModel { Nickname: "bert" }))) yield return s;
        foreach (var s in Dialog("move user", x => _ = User(x, "bert").MoveCommand.ExecuteAsync(null))) yield return s;
        yield return new("move a user", x => x.Server.Apply(new UserUpdated(Bert(FakeServers.Raid))));
        yield return new("mute a user", x =>
        {
            User(x, "bert").ToggleServerMuteCommand.Execute(null);
            x.Server.Apply(new UserUpdated(Bert(FakeServers.Raid, serverMuted: true)));
        });
        yield return new("lift a user's mute", x =>
        {
            User(x, "bert").ToggleServerMuteCommand.Execute(null);
            x.Server.Apply(new UserUpdated(Bert(FakeServers.Raid)));
        });
        foreach (var s in Dialog("kick", x => _ = User(x, "bert").KickCommand.ExecuteAsync(null))) yield return s;
        yield return new("kick a user", x => x.Server.Apply(new UserLeft(3)));
        foreach (var s in Dialog("ban", x => _ = User(x, "anna").BanCommand.ExecuteAsync(null))) yield return s;
        yield return new("ban a user", x => x.Server.Apply(new UserLeft(2)));
        yield return new("mute oneself", x => x.Server.ToggleMuteCommand.Execute(null));
        yield return new("unmute oneself", x => x.Server.ToggleMuteCommand.Execute(null));
        yield return new("deafen oneself", x => x.Server.ToggleDeafenCommand.Execute(null));
        yield return new("undeafen oneself", x => x.Server.ToggleDeafenCommand.Execute(null));
        yield return new(Idle, _ => { }, x => x.Main, Away); // AC2: nothing moves by itself, so a count is not a stray

        // ---- chat ----
        yield return new("send a chat message", x =>
        {
            x.Vm.Chat!.Draft = "Hallo zusammen";
            x.Vm.Chat.SendCommand.Execute(null);
        });
        yield return new("switch the chat tab", x => x.Vm.Chat!.Selected = x.Vm.Chat.Tabs[1]);
        yield return new("switch the chat tab back", x => x.Vm.Chat!.Selected = x.Vm.Chat.Tabs[0]);

        // ---- administration ----
        yield return new("open the administration", x => _ = x.Vm.OpenAdminAsync());
        yield return new("add a group", x => x.Admin.NewGroupCommand.Execute(null));
        yield return new("drop the new group", x => x.Admin.DeleteGroupCommand.Execute(null));
        yield return new("create a group", x => x.Server.Apply(new GroupsChanged([Guest, AdminGroup, Moderator])));
        yield return new("choose a group", x => x.Admin.SelectedGroup = x.Admin.Groups.Single(g => g.Name == "Moderator"), x => Named<ListBox>(x, "GroupList"));
        yield return Toggle("toggle a group's right", x => Shown<CheckBox>(x, c => c.DataContext is PermissionToggle { Permission: Permission.UserMute }));
        yield return Click("save a group", x => Named<Button>(x, "SaveGroupButton"), x =>
        {
            Watch(250); // busy until the server's groups come
            x.Server.Apply(new GroupsChanged([Guest, AdminGroup, Moderator with { Permissions = Moderator.Permissions | Permission.UserMute }]));
        });
        yield return new("reorder groups", x =>
        {
            x.Admin.SelectedGroup = x.Admin.Groups.Single(g => g.Name == "Moderator");
            x.Admin.MoveGroupUpCommand.Execute(null);
            Watch(250);
            x.Server.Apply(new GroupsChanged([Guest, Moderator, AdminGroup])); // the server confirms the order
        });
        yield return new("delete a group", x =>
        {
            x.Admin.SelectedGroup = x.Admin.Groups.Single(g => g.Name == "Moderator");
            _ = x.Admin.DeleteGroupCommand.ExecuteAsync(null);
            x.Server.Apply(new GroupsChanged([Guest, AdminGroup]));
        });

        yield return new("switch to the users", x => AdminTabs(x).SelectedIndex = 1);
        yield return new("refresh the lists", x =>
        {
            _ = x.Admin.RefreshCommand.ExecuteAsync(null);
            x.Server.Apply(new UserList("u", [KnownMe, KnownAnna(muted: true)]));
            x.Server.Apply(new LogList("l", [ServerLog]));
        });
        foreach (var name in new[] { "StatusFilter", "GroupFilter", "SortOrder" }) yield return Drop($"the user list's {name}", x => Named<ComboBox>(x, name));
        yield return new("lift a stored mute", x =>
        {
            Anna(x).LiftMuteCommand.Execute(null);
            Watch(250); // the card waits for a user list that shows it
            x.Server.Apply(new UserList(LastUserList(x), [KnownMe, KnownAnna(muted: false)]));
        });
        foreach (var s in Dialog("delete user", x => _ = Anna(x).DeleteCommand.ExecuteAsync(null))) yield return s;
        yield return new("delete a user", x => x.Server.Apply(new UserList(LastUserList(x), [KnownMe])));

        yield return new("switch to the bans", x => AdminTabs(x).SelectedIndex = 2);
        foreach (var name in new[] { "BanStatusFilter", "BanTypeFilter", "BanSortOrder" }) yield return Drop($"the ban list's {name}", x => Named<ComboBox>(x, name));
        yield return new("ban list shows the ban", x => x.Server.Apply(new BanList("b", [new BanInfo(AnnasBan, "fp2", "anna", null, "Spam", "ich", null)])));
        yield return new("unban a user", x =>
        {
            x.Admin.Bans.Single().UnbanCommand.Execute(null);
            x.Server.Apply(new BanList("b2", []));
        });

        yield return new("switch to the links", x => AdminTabs(x).SelectedIndex = 3);
        Func<Walk, ToggleButton> rowBox = x => Shown<CheckBox>(x, c => c.DataContext is LinkRowViewModel);
        Func<Walk, ToggleButton> cellBox = x => Shown<CheckBox>(x, c => c.DataContext is LinkCellViewModel);
        yield return Toggle("select a channel in the link matrix", rowBox, back: true);
        yield return Toggle("change a link in the matrix", cellBox);
        yield return Click("discard the link changes", x => CommandButton(x, x.Admin.Links.DiscardCommand));
        yield return Toggle("change a link in the matrix again", cellBox);
        yield return Click("apply the link changes", x => Named<Button>(x, "ApplyLinksButton"), x =>
        {
            Watch(250); // busy until the server's links match
            x.Server.Apply(new ChannelsUnlinked(FakeServers.Lobby, FakeServers.Raid));
        });

        yield return new("switch to the server", x => AdminTabs(x).SelectedIndex = 4, Prepare: x => x.Server.Apply(new ServerSettingsChanged(
            x.Server.Mirror.Settings with { Limits = new ServerLimits(50, 30, true, false, new TimeOnly(4, 0)) }))); // with limits: all its check boxes
        var serverBoxes = AdminPage(w).GetVisualDescendants().OfType<CheckBox>().Where(c => c.IsEffectivelyVisible && c.IsEffectivelyEnabled).ToList();
        foreach (var box in serverBoxes)
            yield return Toggle($"server settings: {box.Content as string ?? box.Name}", _ => box, back: true);
        yield return Click("save the server settings", x => CommandButton(x, x.Admin.SaveServerSettingsCommand),
            x => x.Server.Apply(new ServerSettingsChanged(x.Server.Mirror.Settings)));
        yield return new("upload a logo", x =>
        {
            var png = TestImages.Encode(64, 64);
            _ = x.Admin.UploadIconAsync(png, null);
            Watch(250); // the logo waits for the server
            x.Server.Apply(new ServerSettingsChanged(x.Server.Mirror.Settings));
            x.Server.IconPng = png;
        });
        yield return new("create a backup", x =>
        {
            x.Admin.NewBackupCommand.Execute(null);
            Watch(250); // the button waits for the server
            x.Server.Apply(new BackupList("k", [new BackupInfo("backup-1.zip", DateTimeOffset.Now, 1024, "1.0")]));
        });
        foreach (var s in Dialog("restore backup", x => _ = x.Admin.Backups.Single().RestoreCommand.ExecuteAsync(null))) yield return s;
        foreach (var s in Dialog("backup download warning", x => _ = x.Admin.ConfirmDownloadAsync())) yield return s;
        foreach (var s in Dialog("delete backup", x => _ = x.Admin.Backups.Single().DeleteCommand.ExecuteAsync(null))) yield return s;
        yield return new("delete a backup", x => x.Server.Apply(new BackupList("k2", [])));

        yield return new("switch to the logs", x => AdminTabs(x).SelectedIndex = 5);
        yield return Drop("the log source filter", x => Named<ComboBox>(x, "LogSourceFilter"));
        yield return Click("search the logs", x => Named<Button>(x, "LogSearchButton"), x =>
        {
            Watch(250); // the search waits for the server
            x.Server.Apply(new LogSearchResult(null, [new LogHit("server-1", 1, "Start")], false, false));
        }, x => x.Admin.Logs.SearchText = "Start");
        yield return new("open a log file", x =>
        {
            x.Admin.Logs.SelectedFile = x.Admin.Logs.Files.Single();
            x.Server.Apply(new LogPage(null, "server-1", 1, 2, 1, ["Start", "Zeile 2"]));
        });
        yield return new("turn the log page", x =>
        {
            x.Admin.Logs.NewerCommand.Execute(null);
            x.Server.Apply(new LogPage(null, "server-1", 2, 2, 3, ["Zeile 3"]));
        });
        yield return new("close the administration", x => x.Vm.ClosePage());

        // ---- narrow window: the drawer ----
        yield return new("open the drawer", x => x.Main.GetVisualDescendants().OfType<Button>()
            .Single(b => b.IsEffectivelyVisible && AutomationProperties.GetName(b) == Strings.Ui_ShowChannels)
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)), Prepare: x => x.Main.Width = 360);
        yield return new("close the drawer", x => x.Main.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null));

        yield return new("disconnect", x => x.Vm.Server = null, Prepare: x => x.Main.Width = 1100);
    }

    // ---- the tests ----

    /// <summary>AC1, AC2: every action animates in the animated display and ends as in the simplified one.</summary>
    [AvaloniaFact]
    public void EveryAction_AnimatesInAnimatedMode()
    {
        var animated = WalkThrough(DisplayMode.Animated);
        var simplified = WalkThrough(DisplayMode.Simplified);
        Assert.Equal(simplified.Select(r => r.Name), animated.Select(r => r.Name));
        Assert.True(animated.Count > 60, $"the walk covers every action ({animated.Count})");
        var idle = animated.Single(r => r.Name == Idle);
        Assert.True(idle.Started == 0 && !idle.Moved, $"nothing moves by itself ({idle.Started} started, moved: {idle.Movers})");
        var still = animated.Where(r => r.Name != Idle && r.Started == 0 && !r.Moved).Select(r => r.Name).ToList();
        Assert.True(still.Count == 0, "nothing moves for:\n" + string.Join("\n", still));
        var different = animated.Zip(simplified).Where(p => p.First.State != p.Second.State)
            .Select(p => $"{p.First.Name}:\n{p.First.State}--- simplified:\n{p.Second.State}").ToList();
        Assert.True(different.Count == 0, "another end state:\n" + string.Join("\n", different));
    }

    /// <summary>AC3: the simplified display starts none of the new animations.</summary>
    [AvaloniaFact]
    public void EveryAction_NoNewAnimationInSimplifiedMode()
    {
        var simplified = WalkThrough(DisplayMode.Simplified);
        Assert.Empty(simplified.Where(r => r.Started > 0 || r.Moved).Select(r => $"{r.Name}: {r.Started} started, moved: {r.Movers}"));
    }

    /// <summary>
    /// AC4: 50 users join and 50 leave within one second; the tree is right and every animation over within
    /// Motion.Slow + 8 x 30 ms after the last change, and nothing is queued beyond that.
    /// <para>
    /// The changes are scheduled 8 ms apart (the last one at 792 ms) with frames in between as the machine manages; how
    /// long the storm really takes is the test machine's, not the client's, so it is not asserted. The bound is wall
    /// clock time, but an animation can only end on a frame and a removal only when the UI thread gets to it: on a loaded
    /// machine (the suite, or a game beside it) frames come late. So the margin is 300 ms plus four times the longest gap
    /// between two frames seen while it settles (on an idle machine about 350 ms in all).
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public void JoinLeaveStorm_EndsBounded()
    {
        string dir = Directory.CreateTempSubdirectory("ovs-storm-").FullName;
        var vm = new MainViewModel(dir, a => Dispatcher.UIThread.Post(a), useAudioDevices: false);
        var main = new MainWindow { DataContext = vm, Width = 1100, Height = 700 };
        main.Show();
        var server = FakeServers.Crowded();
        server.Post = a => Dispatcher.UIThread.Post(a); // as the main view model does: leaving rows go on the UI thread
        vm.Server = server;
        Watch(800);
        Motion.Apply(main, DisplayMode.Animated);
        var before = server.Channels.ToDictionary(c => c.Name, c => string.Join(",", c.Users.Select(u => u.Nickname)));
        var tree = main.FindControl<ItemsControl>("ChannelItems")!;
        int running = Motion.Running;
        Motion.ResetStarted();

        // 100 changes spread over 800 ms, with frames in between (as many as the machine manages)
        var channels = server.Channels.Select(c => c.Id).ToList();
        Message Change(int i) => i < 50
            ? new UserJoined(new UserInfo((uint)(100 + i), $"fp{100 + i}", $"Gast{i}", channels[i % channels.Count], false, false, false,
                Permission.None, [WellKnownGroups.Guest]))
            : new UserLeft((uint)(100 + i - 50));
        var storm = Stopwatch.StartNew();
        for (int i = 0; i < 100;)
        {
            while (i < 100 && i * 8 <= storm.ElapsedMilliseconds) server.Apply(Change(i++));
            if (i == 100) break; // the last change: the bound starts here
            Frame();
            Thread.Sleep(1);
        }
        long stormMs = storm.ElapsedMilliseconds;
        Assert.True(Motion.Started > 50, $"the storm animated ({Motion.Started})");

        var bound = Motion.Slow + TimeSpan.FromMilliseconds(8 * ItemMotion.StaggerMs);
        bool Settled() => Motion.Running <= running
            && server.Channels.All(c => before[c.Name] == string.Join(",", c.Users.Select(u => u.Nickname)))
            && tree.GetVisualDescendants().OfType<ContentPresenter>().Where(p => p.DataContext is UserViewModel && p.Parent is Panel)
                .All(p => p.Opacity == 1 && !p.Classes.Contains("entering") && !p.Classes.Contains("leaving"));
        var after = Stopwatch.StartNew();
        var gap = TimeSpan.Zero; // the longest time between two frames
        while (!Settled() && after.Elapsed < bound + TimeSpan.FromSeconds(5))
        {
            var frameStart = after.Elapsed;
            Frame();
            Thread.Sleep(1);
            if (after.Elapsed - frameStart > gap) gap = after.Elapsed - frameStart;
        }
        var took = after.Elapsed;
        var margin = TimeSpan.FromMilliseconds(300) + 4 * gap;
        Assert.True(Settled(), $"the tree is right and nothing runs any more ({Motion.Running} running, {running} before)");
        Assert.True(took <= bound + margin, $"settled {took.TotalMilliseconds:F0} ms after the last change (bound {bound.TotalMilliseconds} ms "
            + $"+ {margin.TotalMilliseconds:F0} ms; longest frame gap {gap.TotalMilliseconds:F0} ms, the storm took {stormMs} ms)");

        Motion.ResetStarted();
        Watch(500);
        Assert.Equal(0, Motion.Started); // nothing was queued for later
        Assert.True(Motion.Running <= running);
        main.Close();
        try
        {
            Directory.Delete(dir, true);
        }
        catch (IOException)
        {
        }
    }
}
