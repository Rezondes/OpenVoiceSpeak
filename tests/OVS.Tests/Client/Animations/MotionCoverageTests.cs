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
using OVS.Client.Animations;
using OVS.Client.Dialogs;
using OVS.Client.Localization;
using OVS.Client.Net.Updates;
using OVS.Client.Net;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Client.Views;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Groups;
using OVS.Shared.Protocol.Messages.Links;
using OVS.Shared.Protocol.Messages.LogAccess;
using OVS.Shared.Protocol.Messages.Moderation;
using OVS.Shared.Protocol.Messages.ServerBackups;
using OVS.Shared.Protocol.Messages.ServerInfo;
using OVS.Shared.Protocol.Messages.Users;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Animations;

/// <summary>
/// Package 109: one walk through every user action of the client, in both displays. In the animated display each
/// action starts at least one animation (counted by <see cref="AnimationCounter.Started"/>; for effects that only a style plays,
/// a hover or a check mark, the target is seen passing through in-between values), in the simplified one none, and
/// both end in the same state.
/// <para>
/// What the walk cannot drive headless, and why (AC1):
/// <list type="bullet">
/// <item>Picking a file (server logo, backup upload, own sounds): an OS file picker. What follows the pick is in the
/// walk ("upload a logo" from <see cref="ServerIconViewModel.UploadIcon"/>).</item>
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
[Collection(MotionCollection.Name)]
public sealed class MotionCoverageTests
{
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

    sealed class Walk(MainWindow main, MainViewModel viewModel)
    {
        public MainWindow Main { get; } = main;
        public MainViewModel Vm { get; } = viewModel;
        public ServerViewModel Server { get; set; } = null!;
        public List<Request> Sent { get; } = [];
        public AdminViewModel Admin => Vm.PageFlow.AdminPage!;
        /// <summary>Animations still running from before the walk (other tests' windows): the walk waits for its own only.</summary>
        public int Running { get; } = AnimationCounter.Running;
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
        var viewModel = MainViewModelSetup.Create(dir, action => Dispatcher.UIThread.Post(action));
        var main = new MainWindow { DataContext = viewModel, Width = 1100, Height = 700 };
        viewModel.Dialogs = OverlayDialogs.Create(main.Overlay, () => viewModel.Server?.Mirror.Groups);
        main.Show();
        Watch(200);
        Motion.Apply(main, display);
        var walk = new Walk(main, viewModel);
        var results = new List<Result>();
        foreach (var step in Steps(walk)) results.Add(Run(walk, step));
        main.Close();
        try
        {
            Directory.Delete(dir, true);
        }
        catch (IOException)
        {
        }
        return walks[display] = results;
    }

    static Result Run(Walk walk, Step step)
    {
        if (step.Prepare is { } prepare)
        {
            prepare(walk);
            Settle(walk, watchLeast: false);
        }
        var target = step.Look?.Invoke(walk);
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
        AnimationCounter.ResetStarted();
        Look();
        step.Act(walk);
        Settle(walk, Look);
        if (step.Undo is { } undo)
        {
            undo(walk);
            Settle(walk, watchLeast: false);
        }
        // a style transition passes through values between its start and its end; a jump has two. Frames may come late
        // and miss a 120 ms transition's middle: a look that changed counts as well when the change ran through a
        // transition of the animated display (Motion.Fast or Motion.Normal long; Fluent's own are shorter, the
        // simplified display has none of these)
        var movers = seen.Where(pair => pair.Value.Count >= 3 || pair.Value.Count == 2 && HasMotionTransition(pair.Key)).Select(pair => $"{pair.Key.GetType().Name} {string.Join(".", (pair.Key as StyledElement)?.Classes ?? [])} {(pair.Key as StyledElement)?.Name}").ToList();
        return new Result(step.Name, AnimationCounter.Started, movers.Count > 0, State(walk), string.Join("; ", movers.Take(5)));
    }

    /// <summary>
    /// Frames until the action is over: no animation started from code runs any more, no list row enters or leaves, and
    /// the end state has held for a moment. The animated display watches an action at least 160 ms (a waiting state
    /// shows after 150 ms, a style transition takes 120 to 220 ms); the simplified one changes at once and gets a few frames.
    /// It gives up after 5 s: under load an action's animations take many frames' time, and a step that never settles
    /// shows as another end state.
    /// </summary>
    /// <param name="watchLeast">False for what only prepares or sets back: no waiting state or transition to see there.</param>
    static void Settle(Walk walk, Action? look = null, bool watchLeast = true)
    {
        bool animated = Motion.IsAnimated(walk.Main);
        int least = !watchLeast ? 0 : animated ? 160 : 20, hold = animated ? 25 : 10;
        var watch = Stopwatch.StartNew();
        string? last = null;
        long since = 0;
        while (watch.ElapsedMilliseconds < 5000)
        {
            Frame();
            look?.Invoke();
            Thread.Sleep(1);
            if (AnimationCounter.Running > walk.Running || Rows(walk).Any(control => control.Classes.Contains("entering") || control.Classes.Contains("leaving")))
            {
                last = null;
                continue;
            }
            var now = State(walk);
            if (now != last)
            {
                last = now;
                since = watch.ElapsedMilliseconds;
            }
            else if (watch.ElapsedMilliseconds >= least && watch.ElapsedMilliseconds - since >= hold) return;
        }
    }

    static List<Control> Rows(Walk walk) =>
        walk.Main.GetVisualDescendants().OfType<ItemsControl>().Where(ItemMotion.GetEnter).SelectMany(itemsControl => itemsControl.GetRealizedContainers()).ToList();

    /// <summary>The mouse leaves the window: nothing is hovered, no tooltip waits to open.</summary>
    static void Away(Walk walk) => walk.Main.MouseMove(new Point(-100, -100));

    static bool HasMotionTransition(Visual visual) =>
        visual.Transitions?.Any(transition => transition is Avalonia.Animation.TransitionBase { Duration: var duration } && (duration == Motion.Fast || duration == Motion.Normal)) == true;

    static string Brush(IBrush? brush) => (brush as ISolidColorBrush)?.Color.ToString() ?? brush?.GetType().Name ?? "";

    static string Looks(Visual visual) => $"{visual.RenderTransform?.Value}|{visual.Opacity:F3}|" + visual switch
    {
        Border border => $"{border.BoxShadow}|{Brush(border.Background)}|{Brush(border.BorderBrush)}",
        ContentPresenter contentPresenter => $"{contentPresenter.BoxShadow}|{Brush(contentPresenter.Background)}|{Brush(contentPresenter.BorderBrush)}",
        _ => "",
    };

    /// <summary>What counts after an action: the models, and that nothing of a movement is left on the screen.</summary>
    static string State(Walk walk)
    {
        var viewModel = walk.Vm;
        var main = walk.Main;
        var text = new StringBuilder();
        text.AppendLine($"page={viewModel.PageFlow.Page} connected={viewModel.IsConnected} connecting={viewModel.Connection.IsConnecting} status={viewModel.Status}");
        var card = (Border)main.Overlay.Children[1];
        text.AppendLine($"dialog={main.Overlay.IsOpen}/{main.Overlay.IsVisible} card={card.Opacity:F2}/{card.RenderTransform?.Value.M11 ?? 1:F2} drawer={main.Classes.Contains("drawer")}");
        text.AppendLine($"bookmarks={string.Join(",", viewModel.Bookmarks.Items.Select(tile => tile.Name))}");
        if (viewModel.Server is { } server)
        {
            foreach (var channel in server.Tree.Channels)
                text.AppendLine($"{channel.Name}{(channel.IsLinked ? " linked" : "")}{(channel.IsCurrent ? " current" : "")} {channel.SlotText}: "
                    + string.Join(",", channel.Users.Select(user => user.Nickname + (user.ServerMuted ? " muted" : ""))));
            text.AppendLine($"self muted={server.SelfState.SelfMuted} deafened={server.SelfState.SelfDeafened}");
        }
        if (viewModel.Chat is { } chat) text.AppendLine($"chat tab={chat.Tabs.IndexOf(chat.Selected)} lines={chat.Selected.Entries.Count}");
        if (viewModel.PageFlow.SettingsPage is { } page)
        {
            var settings = page.ToSettings(viewModel.Settings);
            settings.Display = DisplayMode.Animated; // the one setting the two walks differ in
            text.AppendLine(JsonSerializer.Serialize(settings));
        }
        if (viewModel.PageFlow.AdminPage is { } admin)
        {
            text.AppendLine($"admin tab={AdminTabs(walk).SelectedIndex} groups={string.Join(",", admin.GroupList.Groups.Select(groupEdit => $"{groupEdit.Name} {(int)groupEdit.Permissions}"))} selected={admin.GroupList.SelectedGroup?.Name} "
                + $"users={string.Join(",", admin.UsersAndBans.Users.Select(user => user.Nickname + (user.IsServerMuted ? " muted" : "")))} bans={admin.UsersAndBans.Bans.Count} backups={admin.BackupCard.Backups.Count} logo={admin.ServerIcon.HasIcon}");
            text.AppendLine($"links pending={admin.Links.PendingText} selected={string.Join(",", admin.Links.Rows.Where(linkRow => linkRow.IsSelected).Select(linkRow => linkRow.Name))} "
                + $"server settings={admin.ServerSettings.RemovePassword}/{admin.ServerSettings.LogRotateDaily}/{admin.ServerSettings.AutoRestart}");
            text.AppendLine($"logs hits={admin.Logs.ShowHits}/{admin.Logs.Hits.Count} open={admin.Logs.IsFileOpen} page={admin.Logs.Page} lines={admin.Logs.Lines.Count}");
        }
        // nothing of a movement is left: no picture, ghost or popup in the overlay, every list entry whole
        var rows = Rows(walk);
        text.AppendLine($"overlay={OverlayLayer.GetOverlayLayer(main)?.Children.Count} pictures={main.FindControl<Border>("PageGhost")!.IsVisible}/{main.FindControl<Border>("TreeGhost")!.IsVisible} "
            + $"rows={rows.Count} unsettled={rows.Count(control => control.Opacity < 1 || control.Classes.Contains("entering") || control.Classes.Contains("leaving"))}");
        return text.ToString();
    }

    // ---- what the walk finds on the screen ----

    static readonly Guid Archive = Guid.NewGuid(), AnnasBan = Guid.NewGuid();

    static UserInfo Me(Guid channel) => new(1, "fp1", "ich", channel, false, false, false, Permission.All, [WellKnownGroups.Admin]);

    static UserInfo Bert(Guid channel, bool serverMuted = false) =>
        new(3, "fp3", "bert", channel, false, false, serverMuted, Permission.None, [WellKnownGroups.Guest], CanBeModeratedByMe: true);

    static ChannelViewModel Channel(Walk walk, string name) => walk.Server.Tree.Channels.Single(channel => channel.Name == name);
    static UserViewModel User(Walk walk, string nickname) => walk.Server.Tree.Channels.SelectMany(channel => channel.Users).Single(user => user.Nickname == nickname);

    static Border Row(Walk walk, Func<object?, bool> of) =>
        walk.Main.GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("row") && border.ContextMenu is not null && of(border.DataContext));

    static Button BookmarkButton(Walk walk) => walk.Main.GetVisualDescendants().OfType<Button>().First(button => button.Classes.Contains("bookmark") && button.IsEffectivelyVisible);

    static AdminView AdminPage(Walk walk) => walk.Main.GetVisualDescendants().OfType<AdminView>().Single();
    static TabControl AdminTabs(Walk walk) => AdminPage(walk).FindControl<TabControl>("Tabs")!;
    static T Named<T>(Walk walk, string name) where T : Control => AdminPage(walk).FindControl<T>(name)!;

    /// <summary>A shown control of the administration (the selected tab's content).</summary>
    static T Shown<T>(Walk walk, Func<T, bool> which) where T : Control =>
        AdminPage(walk).GetVisualDescendants().OfType<T>().First(c => c.IsEffectivelyVisible && c.IsEffectivelyEnabled && which(c));

    static Button CommandButton(Walk walk, System.Windows.Input.ICommand command) => Shown<Button>(walk, button => button.Command == command);

    static KnownUserViewModel Anna(Walk walk) => walk.Admin.UsersAndBans.Users.Single(knownUser => knownUser.Nickname == "anna");

    /// <summary>The id of the last request of that type: the server answers under it (answers to other ids reach nobody).</summary>

    static KnownUserInfo KnownMe => new("fp1", "ich", [WellKnownGroups.Admin]);
    static KnownUserInfo KnownAnna(bool muted) => new("fp2", "anna", [WellKnownGroups.Guest], ServerMuted: muted, CanBeModeratedByMe: true);
    static LogFileInfo ServerLog => new("server-1", LogKind.Server, null, null, DateTimeOffset.Now.AddHours(-1), DateTimeOffset.Now, 100);

    static Action<Walk> Show(Func<Walk, Control> control) => walk =>
    {
        Away(walk); // the mouse comes to it afresh, not by scrolling it under the pointer
        control(walk).BringIntoView();
    };

    /// <summary>A drop-down opens and closes; the popup fades and slides (counted), its picture fades when it closes.</summary>
    static Step Drop(string name, Func<Walk, ComboBox> combo) =>
        new($"open and close {name}", walk => combo(walk).IsDropDownOpen = true, null, Show(combo), walk => combo(walk).IsDropDownOpen = false);

    /// <summary>A check box toggles (its mark pops or shrinks, a style), set back afterwards when <paramref name="back"/>.</summary>
    static Step Toggle(string name, Func<Walk, ToggleButton> box, bool back = false)
    {
        ToggleButton? shown = null; // the one toggled, also for the undo
        return new(name, walk => shown!.IsChecked = !shown.IsChecked, walk => shown = box(walk), Show(box),
            back ? walk => shown!.IsChecked = !shown.IsChecked : null);
    }

    /// <summary>
    /// The mouse clicks a button: its face lifts under the mouse and gives way when pressed (styles of the animated display;
    /// Fluent's own press tilt of the button itself is not watched, it belongs to the simplified look too).
    /// </summary>
    static Step Click(string name, Func<Walk, Button> button, Action<Walk>? then = null, Action<Walk>? prepare = null) =>
        new(name, walk =>
        {
            var at = TestSupport.Rows.Center(button(walk), walk.Main);
            walk.Main.MouseMove(at);
            walk.Main.MouseDown(at, MouseButton.Left);
            walk.Main.MouseUp(at, MouseButton.Left);
            then?.Invoke(walk);
        }, walk => button(walk).Presenter, walk =>
        {
            prepare?.Invoke(walk);
            Show(button)(walk);
        }, Away);

    static IEnumerable<Step> Menu(string name, Func<Walk, Control> owner)
    {
        yield return new($"open {name}", walk => owner(walk).ContextMenu!.Open(owner(walk)));
        yield return new($"close {name}", walk => owner(walk).ContextMenu!.Close());
    }

    /// <summary>A dialog opens and is closed again without an answer (cancelled).</summary>
    static IEnumerable<Step> Dialog(string name, Action<Walk> open)
    {
        yield return new($"open the {name} dialog", open);
        yield return new($"close the {name} dialog", walk => walk.Main.Overlay.Close());
    }

    /// <summary>
    /// Every settings control once: check boxes and radio buttons toggle (and are set back), drop-downs open and close,
    /// sliders and buttons are hovered. Rows of a list (the sounds) count once per kind.
    /// </summary>
    static IEnumerable<Step> SettingsControls(Walk walk)
    {
        var view = walk.Main.GetVisualDescendants().OfType<SettingsView>().Single();
        var all = view.GetVisualDescendants().OfType<Control>()
            .Where(control => control is CheckBox or RadioButton or ComboBox or Slider && control.IsEffectivelyVisible && control.IsEffectivelyEnabled).ToList();
        var controls = all.Where(control => control.DataContext is SettingsViewModel)
            .Concat(all.Where(control => control.DataContext is not SettingsViewModel).GroupBy(control => (control.GetType(), control.DataContext?.GetType())).Select(grouping => grouping.First()))
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
                    // its own group (Package 142: the transmission type is a second one)
                    var before = radio.GetVisualParent()!.GetVisualChildren().OfType<RadioButton>().Single(radioButton => radioButton.IsChecked == true);
                    yield return new(name, _ => radio.IsChecked = true, _ => radio, show, _ => before.IsChecked = true);
                    break;
                case CheckBox check:
                    yield return new(name, _ => check.IsChecked = !check.IsChecked, _ => check, show, _ => check.IsChecked = !check.IsChecked);
                    break;
                case ComboBox combo:
                    yield return new(name, _ => combo.IsDropDownOpen = true, null, show, _ => combo.IsDropDownOpen = false);
                    break;
                default: // a slider: its thumb grows under the mouse
                    yield return new(name, otherWalk => otherWalk.Main.MouseMove(TestSupport.Rows.Center(control, otherWalk.Main)), _ => control, show, Away);
                    break;
            }
        }
        var cancel = view.GetVisualDescendants().OfType<Button>().First(button => button.Content is string text && text == Strings.Dlg_Cancel);
        yield return new("settings: hover a button", otherWalk => otherWalk.Main.MouseMove(TestSupport.Rows.Center(cancel, otherWalk.Main)), _ => cancel, Show(_ => cancel), Away);
    }

    /// <summary>AC1: every user action, in the order a user could do them.</summary>
    static IEnumerable<Step> Steps(Walk otherWalk)
    {
        // ---- home ----
        foreach (var step in Dialog("connect", walk => _ = ConnectDialog.ShowAsync(walk.Main.Overlay, walk.Vm.Bookmarks.Saved))) yield return step;
        foreach (var step in Dialog("server password", walk => _ = ServerPasswordDialog.ShowAsync(walk.Main.Overlay, "Gilde"))) yield return step;
        foreach (var step in Dialog("certificate", walk => _ = TofuDialog.ShowAsync(walk.Main.Overlay, new TofuPrompt("gilde.example.org", 7000, "AB:CD:EF:01", TofuResult.Unknown))))
            yield return step;
        foreach (var step in Dialog("update offer", walk => _ = UpdateOfferDialog.ShowAsync(walk.Main.Overlay,
            new UpdateOffer("9.9.9", "v9.9.9", "Neu", DateTimeOffset.Now, new Uri("https://example.org/ovs.exe"), new Uri("https://example.org/ovs.sha256")))))
            yield return step;
        yield return new("connect, and it fails", walk =>
        {
            walk.Vm.Connection.IsConnecting = true;
            Watch(150);
            walk.Vm.Status = "Verbindung fehlgeschlagen";
            walk.Vm.Connection.IsConnecting = false;
        });
        yield return new("add a bookmark", walk =>
        {
            var settings = walk.Vm.Settings;
            settings.Bookmarks.Add(new Bookmark("Gilde", "gilde.example.org", 7000, "ich"));
            walk.Vm.ApplySettings(settings);
        });
        foreach (var step in Menu("a bookmark's menu", BookmarkButton)) yield return step;
        foreach (var step in Dialog("edit bookmark", walk => _ = walk.Vm.Bookmarks.EditAsync(walk.Vm.Settings.Bookmarks[0]))) yield return step;
        foreach (var step in Dialog("delete bookmark", walk => _ = walk.Vm.Bookmarks.DeleteAsync(walk.Vm.Settings.Bookmarks[0]))) yield return step;
        yield return new("remove a bookmark", walk =>
        {
            var settings = walk.Vm.Settings;
            settings.Bookmarks.Clear();
            walk.Vm.ApplySettings(settings);
        });

        // ---- settings ----
        yield return new("open the settings", walk => walk.Vm.PageFlow.OpenSettings());
        foreach (var step in SettingsControls(otherWalk)) yield return step;
        foreach (var step in Dialog("key binding", walk => walk.Vm.PageFlow.SettingsPage!.AddKeyBindingCommand.Execute(null))) yield return step;
        yield return new("close the settings", walk => walk.Vm.PageFlow.ClosePage());

        // ---- connected: channels ----
        yield return new("connect", walk =>
        {
            walk.Server = FakeServers.Admin(walk.Sent, dialogs: walk.Vm.Dialogs.Server);
            walk.Server.Post = action => Dispatcher.UIThread.Post(action); // as the main view model does: leaving rows go on the UI thread
            MainViewModelSetup.ShowServer(walk.Vm, walk.Server);
        });
        foreach (var step in Menu("a channel's menu", walk => Row(walk, d => d is ChannelViewModel { Name: "Lobby" }))) yield return step;
        yield return new("join a channel", walk => walk.Server.Apply(new UserUpdated(Me(FakeServers.Raid))));
        yield return new("switch the channel", walk => walk.Server.Apply(new UserUpdated(Me(FakeServers.Lobby))));
        foreach (var step in Dialog("create channel", walk => _ = Channel(walk, "Lobby").CreateCommand.ExecuteAsync(null))) yield return step;
        yield return new("create a channel", walk => walk.Server.Apply(new ChannelAdded(new ChannelInfo(Archive, "Archiv", "", 2))));
        foreach (var step in Dialog("edit channel", walk => _ = Channel(walk, "Archiv").EditCommand.ExecuteAsync(null))) yield return step;
        yield return new("edit a channel: rename it", walk => walk.Server.Apply(new ChannelUpdated(new ChannelInfo(Archive, "Altes Archiv", "Alte Sachen", 2))));
        yield return new("edit a channel: mute it", walk => walk.Server.Apply(new ChannelUpdated(new ChannelInfo(Archive, "Altes Archiv", "Alte Sachen", 2, IsMuted: true))));
        yield return new("edit a channel: limit its users", walk => walk.Server.Apply(new ChannelUpdated(new ChannelInfo(Archive, "Altes Archiv", "Alte Sachen", 2, true, MaxUsers: 5))));
        yield return new("move a channel", walk => walk.Server.Apply(new ChannelUpdated(new ChannelInfo(Archive, "Altes Archiv", "Alte Sachen", -1, true, MaxUsers: 5))));
        foreach (var step in Dialog("link channels", walk => _ = Channel(walk, "Altes Archiv").LinkCommand.ExecuteAsync(null))) yield return step;
        yield return new("link channels", walk => walk.Server.Apply(new ChannelsLinked(Archive, FakeServers.Lobby)));
        yield return new("unlink channels", walk => walk.Server.Apply(new ChannelsUnlinked(Archive, FakeServers.Lobby)));
        foreach (var step in Dialog("channel password", walk => _ = ChannelPasswordDialog.ShowAsync(walk.Main.Overlay, "Raid"))) yield return step;
        foreach (var step in Dialog("delete channel", walk => _ = Channel(walk, "Altes Archiv").DeleteCommand.ExecuteAsync(null))) yield return step;
        yield return new("delete a channel", walk => walk.Server.Apply(new ChannelRemoved(Archive)));

        // ---- connected: users ----
        yield return new("someone joins", walk => walk.Server.Apply(new UserJoined(Bert(FakeServers.Lobby))));
        foreach (var step in Menu("a user's menu", walk => Row(walk, d => d is UserViewModel { Nickname: "bert" }))) yield return step;
        yield return new("move a user", walk => walk.Server.Apply(new UserUpdated(Bert(FakeServers.Raid))));
        yield return new("mute a user", walk =>
        {
            User(walk, "bert").ToggleServerMuteCommand.Execute(null);
            walk.Server.Apply(new UserUpdated(Bert(FakeServers.Raid, serverMuted: true)));
        });
        yield return new("lift a user's mute", walk =>
        {
            User(walk, "bert").ToggleServerMuteCommand.Execute(null);
            walk.Server.Apply(new UserUpdated(Bert(FakeServers.Raid)));
        });
        foreach (var step in Dialog("kick", walk => _ = User(walk, "bert").KickCommand.ExecuteAsync(null))) yield return step;
        yield return new("kick a user", walk => walk.Server.Apply(new UserLeft(3)));
        foreach (var step in Dialog("ban", walk => _ = User(walk, "anna").BanCommand.ExecuteAsync(null))) yield return step;
        yield return new("ban a user", walk => walk.Server.Apply(new UserLeft(2)));
        yield return new("mute oneself", walk => walk.Server.SelfState.ToggleMuteCommand.Execute(null));
        yield return new("unmute oneself", walk => walk.Server.SelfState.ToggleMuteCommand.Execute(null));
        yield return new("deafen oneself", walk => walk.Server.SelfState.ToggleDeafenCommand.Execute(null));
        yield return new("undeafen oneself", walk => walk.Server.SelfState.ToggleDeafenCommand.Execute(null));
        yield return new(Idle, _ => { }, walk => walk.Main, Away); // AC2: nothing moves by itself, so a count is not a stray

        // ---- chat ----
        yield return new("send a chat message", walk =>
        {
            walk.Vm.Chat!.Draft = "Hallo zusammen";
            walk.Vm.Chat.SendCommand.Execute(null);
        });
        yield return new("switch the chat tab", walk => walk.Vm.Chat!.Selected = walk.Vm.Chat.Tabs[1]);
        yield return new("switch the chat tab back", walk => walk.Vm.Chat!.Selected = walk.Vm.Chat.Tabs[0]);

        // ---- administration ----
        yield return new("open the administration", walk => _ = walk.Vm.PageFlow.OpenAdminAsync());
        yield return new("add a group", walk => walk.Admin.GroupList.NewGroupCommand.Execute(null));
        yield return new("drop the new group", walk => walk.Admin.GroupList.DeleteGroupCommand.Execute(null));
        yield return new("create a group", walk => walk.Server.Apply(new GroupsChanged([FakeServers.GuestGroup, FakeServers.AdminGroup, FakeServers.ModeratorGroup])));
        yield return new("choose a group", walk => walk.Admin.GroupList.SelectedGroup = walk.Admin.GroupList.Groups.Single(groupEdit => groupEdit.Name == "Moderator"), walk => Named<ListBox>(walk, "GroupRows"));
        yield return Toggle("toggle a group's right", walk => Shown<CheckBox>(walk, checkBox => checkBox.DataContext is PermissionToggleViewModel { Permission: Permission.UserMute }));
        yield return Click("save a group", walk => Named<Button>(walk, "SaveGroupButton"), walk =>
        {
            Watch(250); // busy until the server's groups come
            walk.Server.Apply(new GroupsChanged([FakeServers.GuestGroup, FakeServers.AdminGroup, FakeServers.ModeratorGroup with { Permissions = FakeServers.ModeratorGroup.Permissions | Permission.UserMute }]));
        });
        yield return new("reorder groups", walk =>
        {
            walk.Admin.GroupList.SelectedGroup = walk.Admin.GroupList.Groups.Single(groupEdit => groupEdit.Name == "Moderator");
            walk.Admin.GroupList.MoveGroupUpCommand.Execute(null);
            Watch(250);
            walk.Server.Apply(new GroupsChanged([FakeServers.GuestGroup, FakeServers.ModeratorGroup, FakeServers.AdminGroup])); // the server confirms the order
        });
        yield return new("delete a group", walk =>
        {
            walk.Admin.GroupList.SelectedGroup = walk.Admin.GroupList.Groups.Single(groupEdit => groupEdit.Name == "Moderator");
            _ = walk.Admin.GroupList.DeleteGroupCommand.ExecuteAsync(null);
            walk.Server.Apply(new GroupsChanged([FakeServers.GuestGroup, FakeServers.AdminGroup]));
        });

        yield return new("switch to the users", walk => AdminTabs(walk).SelectedIndex = 1);
        yield return new("refresh the lists", walk =>
        {
            _ = walk.Admin.RefreshCommand.ExecuteAsync(null);
            walk.Server.Apply(new UserList(walk.Sent.LastRequestId<ListUsers>(), [KnownMe, KnownAnna(muted: true)]));
            walk.Server.Apply(new LogList(walk.Sent.LastRequestId<ListLogs>(), [ServerLog]));
        });
        foreach (var name in new[] { "StatusFilter", "GroupFilter", "SortOrder" }) yield return Drop($"the user list's {name}", walk => Named<ComboBox>(walk, name));
        yield return new("lift a stored mute", walk =>
        {
            Anna(walk).LiftMuteCommand.Execute(null);
            Watch(250); // the card waits for a user list that shows it
            walk.Server.Apply(new UserList(walk.Sent.LastRequestId<ListUsers>(), [KnownMe, KnownAnna(muted: false)]));
        });
        foreach (var step in Dialog("delete user", walk => _ = Anna(walk).DeleteCommand.ExecuteAsync(null))) yield return step;
        yield return new("delete a user", walk => walk.Server.Apply(new UserList(walk.Sent.LastRequestId<ListUsers>(), [KnownMe])));

        yield return new("switch to the bans", walk => AdminTabs(walk).SelectedIndex = 2);
        foreach (var name in new[] { "BanStatusFilter", "BanTypeFilter", "BanSortOrder" }) yield return Drop($"the ban list's {name}", walk => Named<ComboBox>(walk, name));
        yield return new("ban list shows the ban", walk => walk.Server.Apply(new BanList(walk.Sent.LastRequestId<ListBans>(), [new BanInfo(AnnasBan, "fp2", "anna", null, "Spam", "ich", null)])));
        yield return new("unban a user", walk =>
        {
            walk.Admin.UsersAndBans.Bans.Single().UnbanCommand.Execute(null);
            walk.Server.Apply(new BanList(walk.Sent.LastRequestId<Unban>(), []));
        });

        yield return new("switch to the links", walk => AdminTabs(walk).SelectedIndex = 3);
        Func<Walk, ToggleButton> rowBox = walk => Shown<CheckBox>(walk, checkBox => checkBox.DataContext is LinkRowViewModel);
        Func<Walk, ToggleButton> cellBox = walk => Shown<CheckBox>(walk, checkBox => checkBox.DataContext is LinkCellViewModel);
        yield return Toggle("select a channel in the link matrix", rowBox, back: true);
        yield return Toggle("change a link in the matrix", cellBox);
        yield return Click("discard the link changes", walk => CommandButton(walk, walk.Admin.Links.DiscardCommand));
        yield return Toggle("change a link in the matrix again", cellBox);
        yield return Click("apply the link changes", walk => Named<Button>(walk, "ApplyLinksButton"), walk =>
        {
            Watch(250); // busy until the server's links match
            walk.Server.Apply(new ChannelsUnlinked(FakeServers.Lobby, FakeServers.Raid));
        });

        yield return new("switch to the server", walk => AdminTabs(walk).SelectedIndex = 4, Prepare: walk => walk.Server.Apply(new ServerSettingsChanged(
            walk.Server.Mirror.Settings with { Limits = new ServerLimits(50, 30, true, false, new TimeOnly(4, 0)) }))); // with limits: all its check boxes
        var serverBoxes = AdminPage(otherWalk).GetVisualDescendants().OfType<CheckBox>().Where(checkBox => checkBox.IsEffectivelyVisible && checkBox.IsEffectivelyEnabled).ToList();
        foreach (var box in serverBoxes)
            yield return Toggle($"server settings: {box.Content as string ?? box.Name}", _ => box, back: true);
        yield return Click("save the server settings", walk => CommandButton(walk, walk.Admin.ServerSettings.SaveCommand),
            walk => walk.Server.Apply(new ServerSettingsChanged(walk.Server.Mirror.Settings)));
        yield return new("upload a logo", walk =>
        {
            var png = TestImages.Encode(64, 64);
            walk.Admin.ServerIcon.UploadIcon(png, null);
            Watch(250); // the logo waits for the server
            walk.Server.Apply(new ServerSettingsChanged(walk.Server.Mirror.Settings));
            walk.Server.IconPng = png;
        });
        yield return new("create a backup", walk =>
        {
            walk.Admin.BackupCard.NewBackupCommand.Execute(null);
            Watch(250); // the button waits for the server
            walk.Server.Apply(new BackupList(walk.Sent.LastRequestId<CreateBackup>(), [new BackupInfo("backup-1.zip", DateTimeOffset.Now, 1024, "1.0")]));
        });
        foreach (var step in Dialog("restore backup", walk => _ = walk.Admin.BackupCard.Backups.Single().RestoreCommand.ExecuteAsync(null))) yield return step;
        foreach (var step in Dialog("backup download warning", walk => _ = walk.Admin.BackupCard.ConfirmDownloadAsync())) yield return step;
        foreach (var step in Dialog("delete backup", walk => _ = walk.Admin.BackupCard.Backups.Single().DeleteCommand.ExecuteAsync(null))) yield return step;
        yield return new("delete a backup", walk => walk.Server.Apply(new BackupList(walk.Sent.LastRequestId<ListBackups>(), [])));

        yield return new("switch to the logs", walk => AdminTabs(walk).SelectedIndex = 5);
        yield return Drop("the log source filter", walk => Named<ComboBox>(walk, "LogSourceFilter"));
        yield return Click("search the logs", walk => Named<Button>(walk, "LogSearchButton"), walk =>
        {
            Watch(250); // the search waits for the server
            walk.Server.Apply(new LogSearchResult(walk.Sent.LastRequestId<SearchLogs>(), [new LogHit("server-1", 1, "Start")], false, false));
        }, walk => walk.Admin.Logs.SearchText = "Start");
        yield return new("open a log file", walk =>
        {
            walk.Admin.Logs.SelectedFile = walk.Admin.Logs.Files.Single();
            walk.Server.Apply(new LogPage(walk.Sent.LastRequestId<ReadLog>(), "server-1", 1, 2, 1, ["Start", "Zeile 2"]));
        });
        yield return new("turn the log page", walk =>
        {
            walk.Admin.Logs.NewerCommand.Execute(null);
            walk.Server.Apply(new LogPage(walk.Sent.LastRequestId<ReadLog>(), "server-1", 2, 2, 3, ["Zeile 3"]));
        });
        yield return new("close the administration", walk => walk.Vm.PageFlow.ClosePage());

        // ---- narrow window: the drawer ----
        yield return new("open the drawer", walk => walk.Main.GetVisualDescendants().OfType<Button>()
            .Single(button => button.IsEffectivelyVisible && AutomationProperties.GetName(button) == Strings.Ui_ShowChannels)
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)), Prepare: walk => walk.Main.Width = 360);
        yield return new("close the drawer", walk => walk.Main.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null));

        yield return new("disconnect", walk => MainViewModelSetup.ShowServer(walk.Vm, null), Prepare: walk => walk.Main.Width = 1100);
    }

    // ---- the tests ----

    /// <summary>
    /// AC1, AC2: every action animates in the animated display and ends as in the simplified one. About a minute, so it
    /// runs only with OVS_SLOW_TESTS (<see cref="SlowAvaloniaFactAttribute"/>).
    /// </summary>
    [SlowAvaloniaFact]
    public void EveryAction_AnimatesInAnimatedMode()
    {
        var animated = WalkThrough(DisplayMode.Animated);
        var simplified = WalkThrough(DisplayMode.Simplified);
        Assert.Equal(simplified.Select(result => result.Name), animated.Select(result => result.Name));
        Assert.True(animated.Count > 60, $"the walk covers every action ({animated.Count})");
        var idle = animated.Single(result => result.Name == Idle);
        Assert.True(idle.Started == 0 && !idle.Moved, $"nothing moves by itself ({idle.Started} started, moved: {idle.Movers})");
        var still = animated.Where(result => result.Name != Idle && result.Started == 0 && !result.Moved).Select(result => result.Name).ToList();
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
        Assert.Empty(simplified.Where(result => result.Started > 0 || result.Moved).Select(result => $"{result.Name}: {result.Started} started, moved: {result.Movers}"));
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
    /// <para>Package 122: also on a server with 40 channels and 400 users.</para>
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void JoinLeaveStorm_EndsBounded(bool large)
    {
        string dir = Directory.CreateTempSubdirectory("ovs-storm-").FullName;
        var viewModel = MainViewModelSetup.Create(dir, action => Dispatcher.UIThread.Post(action));
        var main = new MainWindow { DataContext = viewModel, Width = 1100, Height = 700 };
        main.Show();
        var server = large ? FakeServers.Large() : FakeServers.Crowded();
        server.Post = action => Dispatcher.UIThread.Post(action); // as the main view model does: leaving rows go on the UI thread
        MainViewModelSetup.ShowServer(viewModel, server);
        Watch(800);
        Motion.Apply(main, DisplayMode.Animated);
        MotionWait.Connected(main); // under load connecting takes longer: the storm starts on a still tree
        var before = server.Tree.Channels.ToDictionary(channel => channel.Name, channel => string.Join(",", channel.Users.Select(user => user.Nickname)));
        var tree = main.FindControl<ItemsControl>("ChannelItems")!;
        int running = AnimationCounter.Running;
        AnimationCounter.ResetStarted();

        // 100 changes spread over 800 ms, with frames in between (as many as the machine manages)
        var channels = server.Tree.Channels.Select(channel => channel.Id).ToList();
        Message Change(int index) => index < 50
            ? new UserJoined(new UserInfo((uint)(1000 + index), $"fp{1000 + index}", $"Gast{index}", channels[index % channels.Count], false, false, false,
                Permission.None, [WellKnownGroups.Guest]))
            : new UserLeft((uint)(1000 + index - 50)); // above the users already there
        var storm = Stopwatch.StartNew();
        for (int index = 0; index < 100;)
        {
            while (index < 100 && index * 8 <= storm.ElapsedMilliseconds) server.Apply(Change(index++));
            if (index == 100) break; // the last change: the bound starts here
            Frame();
            Thread.Sleep(1);
        }
        long stormMs = storm.ElapsedMilliseconds;
        Assert.True(AnimationCounter.Started > 50, $"the storm animated ({AnimationCounter.Started})");

        var bound = Motion.Slow + TimeSpan.FromMilliseconds(8 * ItemMotion.StaggerMs);
        bool Settled() => AnimationCounter.Running <= running
            && server.Tree.Channels.All(channel => before[channel.Name] == string.Join(",", channel.Users.Select(user => user.Nickname)))
            && tree.GetVisualDescendants().OfType<ContentPresenter>().Where(contentPresenter => contentPresenter.DataContext is UserViewModel && contentPresenter.Parent is Panel)
                .All(contentPresenter => contentPresenter.Opacity == 1 && !contentPresenter.Classes.Contains("entering") && !contentPresenter.Classes.Contains("leaving"));
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
        Assert.True(Settled(), $"the tree is right and nothing runs any more ({AnimationCounter.Running} running, {running} before)");
        Assert.True(took <= bound + margin, $"settled {took.TotalMilliseconds:F0} ms after the last change (bound {bound.TotalMilliseconds} ms "
            + $"+ {margin.TotalMilliseconds:F0} ms; longest frame gap {gap.TotalMilliseconds:F0} ms, the storm took {stormMs} ms)");

        AnimationCounter.ResetStarted();
        Watch(500);
        Assert.Equal(0, AnimationCounter.Started); // nothing was queued for later
        Assert.True(AnimationCounter.Running <= running);
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
