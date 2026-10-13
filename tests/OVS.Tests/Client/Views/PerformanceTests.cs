using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OVS.Client.Animations;
using OVS.Client.Audio;
using OVS.Client.Audio.Playback;
using OVS.Client.ViewModels;
using OVS.Client.Views;
using OVS.Client.Net;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.ServerInfo;
using OVS.Shared.Protocol.Messages.Users;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Views;

/// <summary>
/// Package 122: a server with hundreds of users stays smooth because a message only updates what it changes. The tests
/// count the work done, never the time it takes (timing limits failed on slow CI machines).
/// </summary>
[Collection(MotionCollection.Name)]
public sealed class PerformanceTests() : TempDirectoryTest("ovs-perf-", quiet: true)
{
    /// <summary>
    /// Everything the tree shows and offers, per channel and user in their order, to compare incremental updates with a
    /// full rebuild.
    /// </summary>
    static List<string> Tree(ServerViewModel server) =>
    [
        $"current {server.CurrentChannel?.Id} self {server.Self?.SessionId} rights {server.SelfPermissions} admin {server.IsAdmin}",
        .. server.Tree.Channels.SelectMany(channel => (IEnumerable<string>)
        [
            $"{channel.Id} {channel.Name} {channel.SlotText} current {channel.IsCurrent} default {channel.IsDefault} linked {channel.IsLinked} {channel.LinkedNames} muted {channel.IsMuted} "
            + $"create {channel.CanCreate} edit {channel.CanEdit} delete {channel.CanDelete} link {channel.CanLink} unlink {channel.CanUnlink} up {channel.CanMoveUp} down {channel.CanMoveDown} "
            + $"join {channel.CanJoin} locked {channel.IsLocked} {channel.LockText}",
            .. channel.Users.Select(user => $"  {user.SessionId} {user.Nickname} {user.Fingerprint} in {user.ChannelId} '{user.StatusText}' '{user.GroupNames}' self {user.IsSelf} "
                + $"server {user.ServerMuted} deaf {user.IsDeafened} muted {user.IsSelfMutedOnly} speaking {user.IsSpeaking}/{user.IsSpeakingViaLink} "
                + $"move {user.CanMove} mute {user.CanMute} kick {user.CanKick} ban {user.CanBan} message {user.CanMessage} drag {user.CanDrag} volume {user.VolumePercent} "
                + $"groups {string.Join(",", user.Groups.Select(groupAssignment => $"{groupAssignment.Name}:{groupAssignment.IsChecked}:{groupAssignment.IsEnabled}"))} "
                + $"targets {string.Join(",", user.MoveTargets.Select(moveTarget => moveTarget.Channel.Name))}"),
        ]),
    ];

    [Fact]
    public void UserUpdated_OneUser_OnlyThatUserUpdated()
    {
        var server = FakeServers.Large();
        var changed = new List<Guid>();
        foreach (var channel in server.Tree.Channels) channel.Users.CollectionChanged += (_, _) => changed.Add(channel.Id);
        UserInfo User(uint id) => server.Mirror.Users[id];

        int before = server.Tree.UserUpdates;
        server.Apply(new UserUpdated(User(7) with { SelfMuted = true }));
        Assert.Equal(1, server.Tree.UserUpdates - before);
        Assert.NotEqual("", server.Tree.Channels.SelectMany(channel => channel.Users).Single(user => user.SessionId == 7).StatusText);
        Assert.Empty(changed);

        before = server.Tree.UserUpdates;
        server.Apply(new UserUpdated(User(8) with { Nickname = "Zora" })); // last in its channel now
        Assert.Equal(1, server.Tree.UserUpdates - before);
        var eighth = server.Tree.Channels.Single(channel => channel.Id == User(8).ChannelId);
        Assert.Equal("Zora", eighth.Users[^1].Nickname);
        Assert.Equal([eighth.Id], changed.Distinct());

        changed.Clear();
        before = server.Tree.UserUpdates;
        var from = User(9).ChannelId;
        var to = server.Tree.Channels[20].Id;
        server.Apply(new UserUpdated(User(9) with { ChannelId = to }));
        Assert.Equal(1, server.Tree.UserUpdates - before);
        Assert.Equal(new[] { from, to }.Order(), changed.Distinct().Order());
        Assert.Equal(["9", "11"], new[] { from, to }.Select(id => server.Tree.Channels.Single(channel => channel.Id == id).SlotText));
        Assert.Contains(server.Tree.Channels.Single(channel => channel.Id == to).Users, user => user.SessionId == 9);

        changed.Clear();
        before = server.Tree.UserUpdates;
        server.Apply(new UserJoined(User(10) with { SessionId = 500, Fingerprint = "fp500", Nickname = "Neu" }));
        server.Apply(new UserLeft(11));
        Assert.Equal(1, server.Tree.UserUpdates - before); // the new user only
        Assert.Equal(2, changed.Distinct().Count());

        // the same tree a full rebuild makes
        var incremental = Tree(server);
        server.Rebuild();
        Assert.Equal(incremental, Tree(server));
    }

    /// <summary>A join for a session already shown (in another channel) moves it there instead of showing it twice.</summary>
    [Fact]
    public void UserJoined_KnownSession_ShownOnce()
    {
        var server = FakeServers.Large();
        var to = server.Tree.Channels[30].Id;
        server.Apply(new UserJoined(server.Mirror.Users[9] with { ChannelId = to }));
        Assert.Equal([to], server.Tree.Channels.Where(channel => channel.Users.Any(user => user.SessionId == 9)).Select(channel => channel.Id));
        var incremental = Tree(server);
        server.Rebuild();
        Assert.Equal(incremental, Tree(server));
    }

    /// <summary>
    /// Random messages about others and myself, each shown incrementally on one server and with a full rebuild on a twin:
    /// both trees are the same after every message (fixed seed, so a failure repeats).
    /// </summary>
    [Fact]
    public void Tree_RandomMessages_IncrementalMatchesRebuild()
    {
        var random = new Random(122);
        for (int run = 0; run < 50; run++)
        {
            var welcome = FakeServers.LargeWelcome();
            var incremental = new ServerViewModel(new StateMirror(welcome), _ => Task.CompletedTask, TimeProvider.System);
            var rebuilt = new ServerViewModel(new StateMirror(welcome), _ => Task.CompletedTask, TimeProvider.System);
            uint next = 1000;
            for (int step = 0; step < 30; step++)
            {
                var message = RandomMessage(incremental, random, ref next);
                incremental.Apply(message);
                rebuilt.Apply(message);
                rebuilt.Rebuild();
                var (shown, wanted) = (Tree(incremental), Tree(rebuilt));
                if (!shown.SequenceEqual(wanted))
                    Assert.Fail($"run {run}, step {step}: {message}\n" + string.Join("\n", shown.Except(wanted).Take(5)));
            }
        }
    }

    static Message RandomMessage(ServerViewModel server, Random random, ref uint next)
    {
        var mirror = server.Mirror;
        var me = mirror.Self!;
        var others = mirror.Users.Values.Where(user => user.SessionId != mirror.SelfId).ToList();
        var other = others[random.Next(others.Count)];
        var channels = mirror.Channels.Keys.ToList();
        Guid AnyChannel() => channels[random.Next(channels.Count)];
        IReadOnlyList<Guid> AnyGroups() => mirror.Groups.Where(_ => random.Next(2) == 0).Select(groupInfo => groupInfo.Id).ToList();
        return random.Next(16) switch
        {
            0 => new UserUpdated(other with { SelfMuted = !other.SelfMuted }),
            1 => new UserUpdated(other with { SelfDeafened = !other.SelfDeafened }),
            2 => new UserUpdated(other with { ServerMuted = !other.ServerMuted }),
            3 => new UserUpdated(other with { Nickname = $"Name{random.Next(1000)}" }),
            4 => new UserUpdated(other with { ChannelId = AnyChannel() }),
            5 => new UserUpdated(other with { GroupIds = AnyGroups() }),
            6 => new UserUpdated(other with { CanBeModeratedByMe = !other.CanBeModeratedByMe }),
            7 => new UserJoined(other with { SessionId = next, Fingerprint = $"fp{next++}", Nickname = $"Neu{random.Next(1000)}", ChannelId = AnyChannel() }),
            8 => new UserLeft(other.SessionId),
            9 => new UserUpdated(other with { ChannelId = me.ChannelId }),
            10 => new UserJoined(other with { ChannelId = AnyChannel() }), // a session already there
            11 => new UserUpdated(me with { Nickname = $"ich{random.Next(1000)}" }),
            12 => new UserUpdated(me with { SelfMuted = !me.SelfMuted }),
            13 => new UserUpdated(me with { ChannelId = AnyChannel() }),
            14 => new UserUpdated(me with { Permissions = random.Next(2) == 0 ? Permission.All : Permission.Speak | Permission.UserMove | Permission.GroupsAssign }),
            _ => new UserUpdated(me with { GroupIds = AnyGroups() }),
        };
    }

    [AvaloniaFact]
    public void MuteChange_DoesNotRefitSidebar_RenameDoes()
    {
        var (main, server) = OpenLarge();
        MotionWait.Until(() => true, atLeast: 300); // what connecting queued is done
        int fits = main.SidebarFits;

        foreach (uint id in new uint[] { 5, 6, 7, 8 }) server.Apply(new UserUpdated(server.Mirror.Users[id] with { SelfMuted = true }));
        MotionWait.Until(() => true, atLeast: 300);
        Assert.Equal(fits, main.SidebarFits);

        var lobby = server.Mirror.Channels[server.Tree.Channels[3].Id];
        foreach (var name in new[] { "Ein langer neuer Name", "Noch ein Name", "Der letzte Name" })
            server.Apply(new ChannelUpdated(lobby with { Name = name }));
        MotionWait.Until(() => main.SidebarFits > fits);
        MotionWait.Until(() => true, atLeast: 300);
        Assert.Equal(fits + 1, main.SidebarFits); // three renames in one frame: one fit
        main.Close();
    }

    [Fact]
    public async Task StateChanged_Unrelated_GainTableAndIconHashUntouched()
    {
        var viewModel = MainViewModelSetup.Create(dir, action => action());
        var server = FakeServers.Large();
        MainViewModelSetup.ShowServer(viewModel, server);
        var iconTracker = viewModel.Connection.TrackServerIcon(server, "127.0.0.1", 1);
        int gains = viewModel.EngineSync.GainTableBuilds, iconChecks = iconTracker.Checks;

        server.Apply(new UserUpdated(server.Mirror.Users[5] with { SelfMuted = true }));
        server.Apply(new UserUpdated(server.Mirror.Users[6] with { Nickname = "Neu" }));
        server.Apply(new ChannelUpdated(server.Mirror.Channels[server.Tree.Channels[2].Id] with { Name = "Anders" }));
        Assert.Equal((gains, iconChecks), (viewModel.EngineSync.GainTableBuilds, iconTracker.Checks));

        server.Apply(new UserJoined(server.Mirror.Users[5] with { SessionId = 600, Fingerprint = "fp600" }));
        Assert.Equal(gains + 1, viewModel.EngineSync.GainTableBuilds);
        server.Apply(new UserLeft(600));
        Assert.Equal(gains + 2, viewModel.EngineSync.GainTableBuilds);
        server.Tree.Channels.SelectMany(channel => channel.Users).First(user => user.SessionId == 7).VolumePercent = 50;
        Assert.Equal(gains + 3, viewModel.EngineSync.GainTableBuilds);
        Assert.Equal(iconChecks, iconTracker.Checks);

        server.Apply(new ServerSettingsChanged(server.Mirror.Settings with { IconHash = "abc" }));
        Assert.Equal(iconChecks + 1, iconTracker.Checks);
        Assert.Equal(gains + 3, viewModel.EngineSync.GainTableBuilds);
        await viewModel.DisposeAsync();
    }

    [Fact]
    public void Speaking_ManySpeakers_UpdatesThrottledAndScoped()
    {
        // the mixer's output every 20 ms for a second: 20 speakers, one of them flickering in and out every frame
        var reports = new SpeakerReportThrottle();
        var twenty = Enumerable.Range(2, 20).Select(index => new ActiveSpeaker((uint)index, false)).ToList();
        int sent = Enumerable.Range(0, 50).Count(frame => reports.Next(frame % 2 == 0 ? twenty : twenty.Skip(1).ToList()) is not null);
        Assert.InRange(sent, 1, 10);
        // nobody talks any more: that is reported once, then nothing
        var silence = Enumerable.Range(0, 50).Select(_ => reports.Next([])).Where(activeSpeakers => activeSpeakers is not null).ToList();
        Assert.Equal([0], silence.Select(activeSpeakers => activeSpeakers!.Count));

        var time = new ManualTimeProvider();
        var server = FakeServers.Large(time);
        int touched = server.Tree.SpeakingTouches;
        server.Tree.OnSpeakers(twenty);
        Assert.Equal(20, server.Tree.SpeakingTouches - touched);
        Assert.Equal(20, server.Tree.Channels.SelectMany(channel => channel.Users).Count(user => user.IsSpeaking));

        touched = server.Tree.SpeakingTouches;
        time.Advance(TimeSpan.FromMilliseconds(100));
        server.Tree.OnSpeakers(twenty); // the same ones go on talking
        time.Advance(TimeSpan.FromMilliseconds(100));
        server.Tree.OnSpeakers(twenty.Skip(1).ToList()); // the first one stopped
        Assert.Equal(0, server.Tree.SpeakingTouches - touched);

        time.Advance(TimeSpan.FromMilliseconds(250)); // past the first one's hold only
        server.Tree.RefreshSpeaking();
        Assert.Equal(1, server.Tree.SpeakingTouches - touched);
        Assert.Equal(19, server.Tree.Channels.SelectMany(channel => channel.Users).Count(user => user.IsSpeaking));
    }

    [AvaloniaFact]
    public void PlaceTracking_NoMove_NotDone()
    {
        var (main, server) = OpenLarge();
        var lists = main.GetVisualDescendants().OfType<ItemsControl>().Where(ItemMotion.GetFlip).ToList();
        Assert.True(lists.Count > 40, "the tree and every channel's users");
        MotionWait.Until(() => !lists.Any(ItemMotion.TracksPlaces)); // the build-up settles
        Assert.DoesNotContain(lists, ItemMotion.TracksPlaces);

        bool tracked = false;
        foreach (uint id in new uint[] { 5, 6, 7, 8 })
        {
            server.Apply(new UserUpdated(server.Mirror.Users[id] with { SelfMuted = true }));
            server.Tree.OnSpeakers([new ActiveSpeaker(id, false)]);
            MotionWait.Until(() => tracked |= lists.Any(ItemMotion.TracksPlaces), atLeast: 100); // icons and rings change, layout passes run
        }
        Assert.False(tracked, "nothing moved, no list tracked places");

        server.Apply(new UserUpdated(server.Mirror.Users[9] with { ChannelId = server.Tree.Channels[20].Id }));
        Assert.Equal(3, lists.Count(ItemMotion.TracksPlaces)); // the two channels and the tree around them
        main.Close();
    }

    LargeServerScene OpenLarge()
    {
        var viewModel = MainViewModelSetup.Create(dir, action => Dispatcher.UIThread.Post(action));
        var main = new MainWindow { DataContext = viewModel, Width = 1100, Height = 700 };
        main.Show();
        var server = FakeServers.Large();
        server.Post = action => Dispatcher.UIThread.Post(action); // as the main view model does
        MainViewModelSetup.ShowServer(viewModel, server);
        MotionWait.Until(() => true, atLeast: 800);
        MotionWait.Connected(main);
        return new(main, server);
    }

    sealed record LargeServerScene(MainWindow Main, ServerViewModel Server);
}
