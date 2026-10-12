using OVS.Client.Collections;
using OVS.Client.Dialogs;
using OVS.Client.Localization;
using OVS.Client.Audio.Playback;
using OVS.Client.Audio.Sounds;
using OVS.Client.ViewModels;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Chat;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.Groups;
using OVS.Shared.Protocol.Messages.Links;
using OVS.Shared.Protocol.Messages.Moderation;
using OVS.Shared.Protocol.Messages.Users;
using OVS.Shared.Voice;
using OVS.Tests.TestSupport;
using static OVS.Tests.TestSupport.Snapshots;
using P = OVS.Shared.Permissions.Permission;

namespace OVS.Tests.Client.ViewModels;

public class ServerViewModelTests
{
    static readonly Guid Lobby = Guid.NewGuid(), Alpha = Guid.NewGuid(), Bravo = Guid.NewGuid();
    const P Moderator = P.Speak | P.SpeakLinked | P.ChannelLink | P.UserMove | P.UserMute | P.UserKick | P.UserBan;

    sealed record Fixture(ServerViewModel Vm, List<Request> Sent, ManualTimeProvider Time)
    {
        public ChannelViewModel Channel(Guid id) => Vm.Tree.Channels.Single(c => c.Id == id);
        public UserViewModel User(uint id) => Vm.Tree.Channels.SelectMany(c => c.Users).Single(u => u.SessionId == id);
    }

    static Fixture Create(P selfPerms = P.Speak, IReadOnlyList<Guid>? selfGroups = null, ServerDialogs? dialogs = null, IReadOnlyList<GroupInfo>? groups = null,
        params UserInfo[] others)
    {
        var snapshot = Snapshot("Server", "Hallo", Lobby,
            [new ChannelInfo(Lobby, "Lobby", "", 0), new ChannelInfo(Bravo, "Bravo", "", 1), new ChannelInfo(Alpha, "alpha", "", 1)],
            groups ?? [new GroupInfo(WellKnownGroups.Guest, "Gast", P.Speak), new GroupInfo(WellKnownGroups.Admin, "Admin", P.All)],
            [User(1, "ich", Lobby, selfPerms, selfGroups), .. others]);
        var sent = new List<Request>();
        var time = new ManualTimeProvider();
        var vm = new ServerViewModel(Mirror(snapshot), r =>
        {
            sent.Add(r);
            return Task.CompletedTask;
        }, time, dialogs);
        return new Fixture(vm, sent, time);
    }

    [Fact]
    public void Build_SortsChannelsAndGroupsUsers()
    {
        var f = Create(others: [User(2, "zora", Lobby, P.Speak), User(3, "Anna", Lobby, P.Speak), User(4, "bob", Bravo, P.Speak)]);
        Assert.Equal(["Lobby", "alpha", "Bravo"], f.Vm.Tree.Channels.Select(c => c.Name));
        Assert.Equal(["Anna", "ich", "zora"], f.Channel(Lobby).Users.Select(u => u.Nickname));
        Assert.Equal(["bob"], f.Channel(Bravo).Users.Select(u => u.Nickname));
        Assert.True(f.Channel(Lobby).IsCurrent);
        Assert.True(f.User(1).IsSelf);
        Assert.Equal("Server", f.Vm.ServerName);
    }

    /// <summary>Package 51: the volume per person, its icon and tooltip; nobody adjusts their own voice.</summary>
    [Fact]
    public void UserVolume_ShownPerUser_NotForSelf()
    {
        var f = Create(others: [User(2, "anna", Lobby, P.Speak), User(3, "bert", Lobby, P.Speak), User(4, "carla", Lobby, P.Speak)]);
        var volumes = new Dictionary<string, float> { ["fp2"] = 1.5f, ["fp3"] = 0f };
        var changed = new List<(string, float)>();
        f.Vm.VolumeOf = fp => volumes.GetValueOrDefault(fp, 1f);
        f.Vm.VolumeChanged += (fp, v) => changed.Add((fp, v));
        f.Vm.Apply(new UserUpdated(User(4, "carla", Lobby, P.Speak))); // rebuild with the volumes known

        var (anna, bert, carla, me) = (f.User(2), f.User(3), f.User(4), f.User(1));
        Assert.Equal((150d, true, false, "Lautstärke 150 %"), (anna.VolumePercent, anna.IsVolumeChanged, anna.IsLocallyMuted, anna.VolumeText));
        Assert.Equal((0d, false, true), (bert.VolumePercent, bert.IsVolumeChanged, bert.IsLocallyMuted));
        Assert.Equal((100d, false, false), (carla.VolumePercent, carla.IsVolumeChanged, carla.IsLocallyMuted));
        Assert.Equal("Lautstärke von anna", anna.VolumeLabel);
        Assert.True(anna.CanAdjustVolume);
        Assert.False(me.CanAdjustVolume);
        Assert.Empty(changed); // showing the stored value is no change

        carla.VolumePercent = 80;
        Assert.True(carla.IsVolumeChanged);
        anna.ResetVolumeCommand.Execute(null);
        Assert.Equal(100, anna.VolumePercent);
        Assert.Equal([("fp4", 0.8f), ("fp2", 1f)], changed);
        carla.VolumePercent = 250; // the slider cannot, a binding might
        Assert.Equal(200, carla.VolumePercent);
    }

    [Fact]
    public void CurrentChannelAndSelf_FollowOwnUser()
    {
        var f = Create();
        Assert.Equal(Lobby, f.Vm.CurrentChannel!.Id);
        Assert.Equal("ich", f.Vm.Self!.Nickname);

        f.Vm.Apply(new UserUpdated(User(1, "ich", Bravo, P.Speak)));
        Assert.Equal(Bravo, f.Vm.CurrentChannel!.Id);
        Assert.Same(f.User(1), f.Vm.Self);
    }

    [Fact]
    public void StatusIcons_MicOffOnlyForAPlainMute()
    {
        var f = Create(others:
        [
            new UserInfo(2, "fp2", "stumm", Lobby, true, false, false, P.Speak, []),
            new UserInfo(3, "fp3", "taub", Lobby, true, true, false, P.Speak, []),
            new UserInfo(4, "fp4", "server", Lobby, true, false, true, P.Speak, []),
        ]);
        Assert.True(f.User(2).IsSelfMutedOnly);
        Assert.False(f.User(2).IsDeafened);
        Assert.False(f.User(3).IsSelfMutedOnly);
        Assert.True(f.User(3).IsDeafened);
        Assert.False(f.User(4).IsSelfMutedOnly);
        Assert.True(f.User(4).ServerMuted);
    }

    [Fact]
    public void UserUpdated_MovesUser_KeepsInstance()
    {
        var f = Create(others: User(2, "anna", Lobby, P.Speak));
        var anna = f.User(2);
        f.Vm.Apply(new UserUpdated(User(2, "anna", Alpha, P.Speak)));
        Assert.DoesNotContain(anna, f.Channel(Lobby).Users);
        Assert.Same(anna, Assert.Single(f.Channel(Alpha).Users));
    }

    [Fact]
    public void UserJoinedAndLeft_UpdateTree()
    {
        var f = Create();
        f.Vm.Apply(new UserJoined(User(5, "neu", Bravo, P.Speak)));
        Assert.Single(f.Channel(Bravo).Users);
        f.Vm.Apply(new UserLeft(5));
        Assert.Empty(f.Channel(Bravo).Users);
    }

    [Fact]
    public void Speaking_ClearsAfter300ms()
    {
        var f = Create(others: User(2, "anna", Lobby, P.Speak));
        f.Vm.Tree.OnSpeakers([new ActiveSpeaker(2, false)]);
        Assert.True(f.User(2).IsSpeaking);

        f.Time.Advance(TimeSpan.FromMilliseconds(200));
        f.Vm.Tree.RefreshSpeaking();
        Assert.True(f.User(2).IsSpeaking);

        f.Time.Advance(TimeSpan.FromMilliseconds(150));
        f.Vm.Tree.RefreshSpeaking();
        Assert.False(f.User(2).IsSpeaking);
    }

    [Fact]
    public void SpeakingViaLink_Flagged()
    {
        var f = Create(others: User(2, "anna", Alpha, P.Speak));
        f.Vm.Tree.OnSpeakers([new ActiveSpeaker(2, true)]);
        Assert.True(f.User(2).IsSpeakingViaLink);
        Assert.False(f.User(2).IsSpeaking);
    }

    [Fact]
    public void SelfTransmitting_ShowsOnOwnEntry()
    {
        var f = Create();
        f.Vm.Tree.SetSelfTransmitting(VoiceHeader.TargetLinked);
        Assert.True(f.User(1).IsSpeakingViaLink);
        f.Vm.Tree.SetSelfTransmitting(null);
        Assert.False(f.User(1).IsSpeakingViaLink);
    }

    [Fact]
    public void LinkedChannels_ShowPartners()
    {
        var f = Create();
        f.Vm.Apply(new ChannelsLinked(Lobby, Alpha));
        f.Vm.Apply(new ChannelsLinked(Lobby, Bravo));
        Assert.True(f.Channel(Lobby).IsLinked);
        Assert.Equal("alpha, Bravo", f.Channel(Lobby).LinkedNames);
        Assert.Equal("Lobby", f.Channel(Alpha).LinkedNames);
    }

    [Fact]
    public async Task JoinCommand_SendsJoinChannel()
    {
        var f = Create();
        await f.Channel(Alpha).JoinCommand.ExecuteAsync(null);
        var join = Assert.IsType<JoinChannel>(Assert.Single(f.Sent));
        Assert.Equal(Alpha, join.ChannelId);
        Assert.NotNull(join.RequestId);
    }

    [Fact]
    public async Task Deafen_SendsSelfState_AndRestoresMute()
    {
        var f = Create();
        await f.Vm.SelfState.ToggleDeafenCommand.ExecuteAsync(null);
        Assert.Equal(new SetSelfState(true, true), f.Sent[^1] with { RequestId = null });
        await f.Vm.SelfState.ToggleDeafenCommand.ExecuteAsync(null);
        Assert.Equal(new SetSelfState(false, false), f.Sent[^1] with { RequestId = null });

        await f.Vm.SelfState.ToggleMuteCommand.ExecuteAsync(null);
        await f.Vm.SelfState.ToggleDeafenCommand.ExecuteAsync(null);
        await f.Vm.SelfState.ToggleDeafenCommand.ExecuteAsync(null);
        Assert.True(f.Vm.SelfState.SelfMuted); // was muted before deafening
        Assert.False(f.Vm.SelfState.SelfDeafened);
    }

    /// <summary>Package 93: the lock icon names the groups; joining is only offered with one of them or as admin.</summary>
    [Fact]
    public async Task LockedChannel_IconTooltipAndJoinAvailability()
    {
        var raid = Guid.NewGuid();
        var f = Create();
        string? notice = null;
        f.Vm.Notice += n => notice = n;
        f.Vm.Apply(new GroupsChanged([new(WellKnownGroups.Guest, "Gast", P.None), new(raid, "Raid", P.None), new(WellKnownGroups.Admin, "Admin", P.None)]));
        f.Vm.Apply(new ChannelUpdated(new ChannelInfo(Bravo, "Bravo", "", 1, AllowedGroupIds: [raid, WellKnownGroups.Admin])));
        var bravo = f.Channel(Bravo);
        Assert.Equal((true, false), (bravo.IsLocked, bravo.CanJoin));
        Assert.Equal("Nur für Gruppen: Raid, Admin", bravo.LockText);
        Assert.Equal((false, true), (f.Channel(Alpha).IsLocked, f.Channel(Alpha).CanJoin));

        await bravo.JoinCommand.ExecuteAsync(null);
        Assert.Empty(f.Sent);
        Assert.Equal(ErrorTexts.For(ReasonCodes.ChannelLocked), notice);

        f.Vm.Apply(new UserUpdated(User(1, "ich", Lobby, P.Speak, [WellKnownGroups.Guest, raid])));
        Assert.True(bravo.CanJoin);
        await bravo.JoinCommand.ExecuteAsync(null);
        Assert.Equal(new JoinChannel(Bravo), f.Sent[^1] with { RequestId = null });

        f.Vm.Apply(new ChannelUpdated(new ChannelInfo(Bravo, "Bravo", "", 1, AllowedGroupIds: [])));
        Assert.Equal((true, false, "Nur für Admins"), (bravo.IsLocked, bravo.CanJoin, bravo.LockText));
        var admin = Create(P.All, [WellKnownGroups.Admin]);
        admin.Vm.Apply(new ChannelUpdated(new ChannelInfo(Bravo, "Bravo", "", 1, AllowedGroupIds: [])));
        Assert.True(admin.Channel(Bravo).CanJoin);
    }

    [Fact]
    public async Task Join_LockedChannelWithoutDialogs_SendsNothing()
    {
        var fixture = Create();
        fixture.Vm.Apply(new ChannelUpdated(new ChannelInfo(Bravo, "Bravo", "", 1, HasPassword: true)));

        await HangGuard.Bound(fixture.Channel(Bravo).JoinCommand.ExecuteAsync(null));

        Assert.Empty(fixture.Sent);
    }

    [Fact]
    public async Task JoinAsync_EmptyPasswordEntered_SendsNothing()
    {
        var fixture = Create(dialogs: new ServerDialogs { AskChannelPassword = _ => Task.FromResult<string?>("") });
        fixture.Vm.Apply(new ChannelUpdated(new ChannelInfo(Bravo, "Bravo", "", 1, HasPassword: true)));

        await HangGuard.Bound(fixture.Vm.JoinAsync(Bravo));

        Assert.Empty(fixture.Sent);
    }

    [Fact]
    public async Task Join_ChannelPasswordDialogThrows_PropagatesAndSendsNothing()
    {
        var fixture = Create(dialogs: new ServerDialogs { AskChannelPassword = _ => throw new InvalidOperationException("Dialog kaputt") });
        fixture.Vm.Apply(new ChannelUpdated(new ChannelInfo(Bravo, "Bravo", "", 1, HasPassword: true)));

        await Assert.ThrowsAsync<InvalidOperationException>(() => HangGuard.Bound(fixture.Channel(Bravo).JoinCommand.ExecuteAsync(null)));

        Assert.Empty(fixture.Sent);
    }

    /// <summary>Package 94: asked once per connection; a wrong password is forgotten, the bypass right and admins are never asked.</summary>
    [Fact]
    public async Task JoinPasswordChannel_PromptsOnce_RememberedForSession()
    {
        var asked = new List<string>();
        string? answer = null;
        var dialogs = new ServerDialogs
        {
            AskChannelPassword = name =>
            {
                asked.Add(name);
                return Task.FromResult(answer);
            },
        };
        var f = Create(dialogs: dialogs);
        f.Vm.Apply(new ChannelUpdated(new ChannelInfo(Bravo, "Bravo", "", 1, HasPassword: true)));
        var bravo = f.Channel(Bravo);
        Assert.Equal((true, true, "Passwort"), (bravo.IsLocked, bravo.CanJoin, bravo.LockText));

        await bravo.JoinCommand.ExecuteAsync(null); // cancelled
        Assert.Empty(f.Sent);
        answer = "falsch";
        await bravo.JoinCommand.ExecuteAsync(null);
        Assert.Equal(new JoinChannel(Bravo, "falsch"), f.Sent[^1] with { RequestId = null });
        f.Vm.Apply(new Error(f.Sent[^1].RequestId, ReasonCodes.WrongChannelPassword));
        answer = "geheim";
        await bravo.JoinCommand.ExecuteAsync(null); // asked again
        Assert.Equal(new JoinChannel(Bravo, "geheim"), f.Sent[^1] with { RequestId = null });
        f.Vm.Apply(new UserUpdated(User(1, "ich", Bravo, P.Speak)));
        Assert.Equal(["Bravo", "Bravo", "Bravo"], asked);

        await f.Channel(Lobby).JoinCommand.ExecuteAsync(null);
        Assert.Equal(new JoinChannel(Lobby), f.Sent[^1] with { RequestId = null });
        f.Vm.Apply(new UserUpdated(User(1, "ich", Lobby, P.Speak)));
        await bravo.JoinCommand.ExecuteAsync(null); // remembered
        Assert.Equal(new JoinChannel(Bravo, "geheim"), f.Sent[^1] with { RequestId = null });
        Assert.Equal(3, asked.Count);

        foreach (var (perms, groups) in new (P, Guid)[] { (P.Speak | P.ChannelPasswordBypass, WellKnownGroups.Guest), (P.All, WellKnownGroups.Admin) })
        {
            var other = Create(perms, [groups], dialogs);
            other.Vm.Apply(new ChannelUpdated(new ChannelInfo(Bravo, "Bravo", "", 1, HasPassword: true)));
            await other.Channel(Bravo).JoinCommand.ExecuteAsync(null);
            Assert.Equal(new JoinChannel(Bravo), other.Sent[^1] with { RequestId = null });
        }
        Assert.Equal(3, asked.Count);

        f.Vm.Apply(new ChannelUpdated(new ChannelInfo(Bravo, "Bravo", "", 1, AllowedGroupIds: [WellKnownGroups.Guest], HasPassword: true)));
        Assert.Equal("Nur für Gruppen: Gast\nPasswort", bravo.LockText);
    }

    [Fact]
    public void Error_BecomesGermanNotice()
    {
        var f = Create();
        string? notice = null;
        f.Vm.Notice += n => notice = n;
        f.Vm.Apply(new Error("r1", ReasonCodes.PermissionDenied));
        Assert.Equal(ErrorTexts.For(ReasonCodes.PermissionDenied), notice);
    }

    [Theory]
    [InlineData(P.Speak, false, false, false)]
    [InlineData(P.ChannelEdit, true, false, false)]
    [InlineData(P.ChannelDelete, false, true, false)]
    [InlineData(P.ChannelLink, false, false, true)]
    [InlineData(P.All, true, true, true)]
    public void ChannelMenu_VisibilityByPermission(P perms, bool edit, bool delete, bool link)
    {
        var f = Create(perms);
        var alpha = f.Channel(Alpha);
        Assert.Equal((edit, delete, link), (alpha.CanEdit, alpha.CanDelete, alpha.CanLink));
        Assert.Equal(perms.Has(P.ChannelCreate), alpha.CanCreate);
        Assert.False(f.Channel(Lobby).CanDelete); // default channel never
        Assert.False(alpha.CanUnlink);
        Assert.Equal(perms.Has(P.ChannelCreate), f.Vm.CanCreateChannel);
    }

    [Fact]
    public void UserMenu_VisibilityByPermissionAndRank()
    {
        // Package 92: the rank comes from the server as a flag, others' rights are never sent
        var f = Create(Moderator, others: [User(2, "gast", Lobby, P.None) with { CanBeModeratedByMe = true }, User(3, "admin", Lobby, P.None)]);
        var guest = f.User(2);
        Assert.True(guest.CanKick && guest.CanBan && guest.CanMute && guest.CanMove);

        var admin = f.User(3);
        Assert.False(admin.CanKick || admin.CanBan || admin.CanMute || admin.CanMove);

        var self = f.User(1);
        Assert.False(self.CanKick || self.CanBan || self.CanMute || self.CanMove); // Package 84: moving oneself is joining

        var plain = Create(others: User(2, "gast", Lobby, P.None) with { CanBeModeratedByMe = true }); // weaker, but no right to act
        Assert.False(plain.User(2).CanKick || plain.User(2).CanMove);
    }

    /// <summary>
    /// Package 84 (A102): the menu offers exactly what the server allows, by the same rule.
    /// Package 92: the server sends the rule's result per recipient (StateSyncTests); here the rights become that flag.
    /// </summary>
    [Fact]
    public void ActionsMatchServerRule()
    {
        var others = new[]
        {
            User(2, "gast", Lobby, P.Speak), User(3, "mod2", Lobby, Moderator), User(4, "admin", Lobby, P.All),
            User(5, "zweit", Lobby, P.Speak) with { Fingerprint = "fp1" }, // another session of the same identity
            User(6, "anders", Lobby, P.Speak | P.ChatServer), // neither contains the other
        };
        var f = Create(Moderator, others: others.Select(u => u with
        {
            Permissions = P.None, CanBeModeratedByMe = Moderator.CanModerate("fp1", u.Permissions, u.Fingerprint),
        }).ToArray());
        foreach (var u in others)
        {
            var allowed = Moderator.CanModerate("fp1", u.Permissions, u.Fingerprint);
            var vm = f.User(u.SessionId);
            Assert.Equal([allowed, allowed, allowed, allowed], [vm.CanKick, vm.CanBan, vm.CanMute, vm.CanMove]);
        }
        Assert.True(f.User(2).CanKick);
        Assert.False(f.User(3).CanKick || f.User(3).CanBan || f.User(3).CanMute || f.User(3).CanMove);
        Assert.False(f.User(5).CanKick);
    }

    [Fact]
    public void LinkCandidates_ExcludesSelfAndLinked()
    {
        var f = Create(P.All);
        f.Vm.Apply(new ChannelsLinked(Lobby, Alpha));
        Assert.Equal([Bravo], f.Vm.Tree.LinkCandidates(f.Channel(Lobby)).Select(c => c.Id));
        Assert.Equal([Alpha], f.Vm.Tree.LinkedChannelVms(f.Channel(Lobby)).Select(c => c.Id));
        Assert.True(f.Channel(Lobby).CanUnlink);
    }

    [Fact]
    public async Task LinkCommand_UsesPickedChannel()
    {
        var dialogs = new ServerDialogs { PickChannel = (_, candidates, _) => Task.FromResult<ChannelViewModel?>(candidates.Single(c => c.Name == "Bravo")) };
        var f = Create(P.All, dialogs: dialogs);
        await f.Channel(Lobby).LinkCommand.ExecuteAsync(null);
        Assert.Equal(new LinkChannels(Lobby, Bravo), f.Sent[^1] with { RequestId = null });
    }

    /// <summary>Package 54: creating opens the same dialog, empty, and sends every option.</summary>
    [Fact]
    public async Task NewChannel_SendsAllOptions()
    {
        (ChannelEdit Current, ChannelDialogMode Mode)? shown = null;
        var dialogs = new ServerDialogs
        {
            EditChannel = (current, mode, _) =>
            {
                shown = (current, mode);
                return Task.FromResult<ChannelEdit?>(new ChannelEdit("Neu", "Beschreibung", IsMuted: true, MaxUsers: 3));
            },
        };
        var f = Create(P.All, dialogs: dialogs);
        await f.Channel(Alpha).CreateCommand.ExecuteAsync(null);
        Assert.Equal((new ChannelEdit("", ""), ChannelDialogMode.Create), shown);
        Assert.Equal(new CreateChannel("Neu", "Beschreibung", IsMuted: true, MaxUsers: 3), f.Sent[^1] with { RequestId = null });
    }

    /// <summary>Package 34: the dialog shows the current flag, the edit sends it, the channel shows it.</summary>
    [Fact]
    public async Task ChannelMuted_EditSendsFlag_ViewModelShowsIt()
    {
        ChannelDialogMode? shownMode = null;
        var dialogs = new ServerDialogs
        {
            EditChannel = (current, mode, _) =>
            {
                shownMode = mode;
                return Task.FromResult<ChannelEdit?>(current with { IsMuted = true });
            },
        };
        var f = Create(P.All, dialogs: dialogs);
        await f.Channel(Bravo).EditCommand.ExecuteAsync(null);
        Assert.Equal(ChannelDialogMode.Edit, shownMode);
        Assert.Equal(new EditChannel(Bravo, "Bravo", "", 1, IsMuted: true), f.Sent[^1] with { RequestId = null });
        await f.Channel(Lobby).EditCommand.ExecuteAsync(null);
        Assert.Equal(ChannelDialogMode.EditDefault, shownMode);

        Assert.False(f.Channel(Bravo).IsMuted);
        f.Vm.Apply(new ChannelUpdated(new ChannelInfo(Bravo, "Bravo", "", 1, IsMuted: true)));
        Assert.True(f.Channel(Bravo).IsMuted);
    }

    /// <summary>Package 36: up and down send the complete order, the ends are disabled.</summary>
    [Fact]
    public async Task MoveUpDown_SendFullOrder_DisabledAtEdges()
    {
        var f = Create(P.ChannelEdit);
        Assert.Equal((false, true), (f.Channel(Lobby).CanMoveUp, f.Channel(Lobby).CanMoveDown));
        Assert.Equal((true, false), (f.Channel(Bravo).CanMoveUp, f.Channel(Bravo).CanMoveDown));

        await f.Channel(Bravo).MoveUpCommand.ExecuteAsync(null);
        Assert.Equal(new[] { Lobby, Bravo, Alpha }, ((ReorderChannels)f.Sent[^1]).ChannelIds);
        f.Vm.Apply(new Error(f.Sent[^1].RequestId, ReasonCodes.PermissionDenied)); // Package 98: refused, the server's order is back
        await f.Channel(Lobby).MoveDownCommand.ExecuteAsync(null);
        Assert.Equal(new[] { Alpha, Lobby, Bravo }, ((ReorderChannels)f.Sent[^1]).ChannelIds);

        var guest = Create();
        Assert.False(guest.Channel(Alpha).CanMoveUp); // without "Channels bearbeiten"
    }

    [Fact]
    public async Task DropOnChannel_SendsOrderWithSourceBeforeOrAfterTarget()
    {
        var f = Create(P.ChannelEdit);
        await f.Vm.Tree.MoveChannelAsync(f.Channel(Bravo), f.Channel(Lobby), after: false);
        Assert.Equal(new[] { Bravo, Lobby, Alpha }, ((ReorderChannels)f.Sent[^1]).ChannelIds);
        f.Vm.Apply(new Error(f.Sent[^1].RequestId, ReasonCodes.PermissionDenied)); // Package 98: refused, the server's order is back
        await f.Vm.Tree.MoveChannelAsync(f.Channel(Lobby), f.Channel(Bravo), after: true);
        Assert.Equal(new[] { Alpha, Bravo, Lobby }, ((ReorderChannels)f.Sent[^1]).ChannelIds);
        f.Vm.Apply(new Error(f.Sent[^1].RequestId, ReasonCodes.PermissionDenied));

        int before = f.Sent.Count;
        await f.Vm.Tree.MoveChannelAsync(f.Channel(Alpha), f.Channel(Lobby), after: true); // already there
        await f.Vm.Tree.MoveChannelAsync(f.Channel(Alpha), f.Channel(Alpha), after: false);
        Assert.Equal(before, f.Sent.Count);
    }

    /// <summary>Package 37: the group tooltip follows the server's group order, not the order of assignment.</summary>
    [Fact]
    public void GroupNames_FollowGroupOrder()
    {
        var f = Create(others: [User(2, "anna", Lobby, P.Speak, groups: [WellKnownGroups.Admin, WellKnownGroups.Guest])]);
        Assert.Equal("Gast, Admin", f.User(2).GroupNames);
        f.Vm.Apply(new GroupsChanged([new GroupInfo(WellKnownGroups.Admin, "Admin", P.All), new GroupInfo(WellKnownGroups.Guest, "Gast", P.Speak)]));
        Assert.Equal("Admin, Gast", f.User(2).GroupNames);
    }

    // ---- Package 47: sounds ----

    static List<OVS.Client.Audio.Sounds.SoundEvent> Sounds(ServerViewModel vm)
    {
        var heard = new List<OVS.Client.Audio.Sounds.SoundEvent>();
        vm.SoundRequested += heard.Add;
        return heard;
    }

    [Fact]
    public void SoundEvents_FromMessages()
    {
        var f = Create(others: [User(2, "anna", Lobby, P.Speak), User(3, "bob", Bravo, P.Speak)]);
        var heard = Sounds(f.Vm);
        f.Vm.Apply(new UserJoined(User(4, "carla", Lobby, P.Speak)));              // into my channel
        f.Vm.Apply(new UserJoined(User(5, "dora", Bravo, P.Speak)));               // elsewhere: nothing
        f.Vm.Apply(new UserUpdated(User(3, "bob", Lobby, P.Speak)));               // comes over to me
        f.Vm.Apply(new UserUpdated(User(2, "anna", Alpha, P.Speak)));              // leaves my channel
        f.Vm.Apply(new UserLeft(4));                                   // gone from my channel
        f.Vm.Apply(new UserLeft(5));                                   // gone elsewhere: nothing
        f.Vm.Apply(new UserUpdated(User(1, "ich", Lobby, P.Speak) with { ServerMuted = true }));
        f.Vm.Apply(new UserUpdated(User(1, "ich", Bravo, P.Speak)));               // moved by someone else
        f.Vm.Apply(new ChatMessage(ChatTarget.Private, 3, "bob", null, 1, "psst", DateTimeOffset.UtcNow));
        f.Vm.Apply(new ChatMessage(ChatTarget.Private, 1, "ich", null, 3, "ja", DateTimeOffset.UtcNow)); // own echo: nothing
        Assert.Equal(
        [
            OVS.Client.Audio.Sounds.SoundEvent.UserJoined, OVS.Client.Audio.Sounds.SoundEvent.UserJoined, OVS.Client.Audio.Sounds.SoundEvent.UserLeft,
            OVS.Client.Audio.Sounds.SoundEvent.UserLeft, OVS.Client.Audio.Sounds.SoundEvent.ServerMuted, OVS.Client.Audio.Sounds.SoundEvent.Moved,
            OVS.Client.Audio.Sounds.SoundEvent.PrivateMessage,
        ], heard);
    }

    /// <summary>Package 73: a change of the own groups gives one tone, the state on connect and other updates none.</summary>
    [Fact]
    public void OwnGroupsChanged_SoundOnce_NotOnConnect()
    {
        var f = Create(others: [User(2, "anna", Lobby, P.Speak)]);
        var heard = Sounds(f.Vm);
        f.Vm.Apply(new UserUpdated(User(1, "ich", Lobby, P.Speak, groups: [WellKnownGroups.Guest]) with { SelfMuted = true })); // no group change
        f.Vm.Apply(new UserUpdated(User(1, "ich", Lobby, P.All, [WellKnownGroups.Guest, WellKnownGroups.Admin]))); // given
        f.Vm.Apply(new UserUpdated(User(1, "ich", Lobby, P.All, [WellKnownGroups.Guest, WellKnownGroups.Admin]))); // repeated: no change
        f.Vm.Apply(new UserUpdated(User(1, "ich", Lobby, P.Speak, [WellKnownGroups.Guest]))); // taken away
        f.Vm.Apply(new UserUpdated(User(2, "anna", Lobby, P.None, [WellKnownGroups.Admin]))); // somebody else (Package 92: without rights)
        Assert.Equal([SoundEvent.GroupChanged, SoundEvent.GroupChanged], heard);
    }

    /// <summary>Package 73: changing the own groups in the admin page gives only the tone for the one acting.</summary>
    [Fact]
    public async Task OwnGroupChangedByMe_OnlyOneSound()
    {
        var f = Create(P.All | P.GroupsAssign, [WellKnownGroups.Guest, WellKnownGroups.Admin]);
        var admin = new AdminViewModel(f.Vm);
        admin.UsersAndBans.Apply(new UserList("r", [new KnownUserInfo("fp1", "ich", [WellKnownGroups.Guest, WellKnownGroups.Admin])]));
        var heard = Sounds(f.Vm);
        var guest = Assert.Single(admin.UsersAndBans.Users).Toggles.Single(t => t.GroupId == WellKnownGroups.Guest);
        guest.IsChecked = false;
        await guest.ToggleCommand.ExecuteAsync(null);
        f.Vm.Apply(new UserUpdated(User(1, "ich", Lobby, P.All, [WellKnownGroups.Admin]))); // the server tells me first
        admin.UsersAndBans.Apply(new UserList("r", [new KnownUserInfo("fp1", "ich", [WellKnownGroups.Admin])]));
        Assert.Equal([SoundEvent.GroupChangedByMe], heard);

        f.Vm.Apply(new UserUpdated(User(1, "ich", Lobby, P.All, [WellKnownGroups.Guest, WellKnownGroups.Admin]))); // someone else gives it back
        Assert.Equal([SoundEvent.GroupChangedByMe, SoundEvent.GroupChanged], heard);
    }

    /// <summary>Package 57: someone talking in over a link is announced by a short tone, once per transmission.</summary>
    [Fact]
    public void LinkVoice_SoundOncePerTransmission()
    {
        var f = Create(others: [User(2, "anna", Bravo, P.Speak)]);
        var heard = Sounds(f.Vm);
        void Talk(int ms)
        {
            for (int t = 0; t < ms; t += 20)
            {
                f.Vm.Tree.OnSpeakers([new ActiveSpeaker(2, ViaLink: true)]);
                f.Time.Advance(TimeSpan.FromMilliseconds(20));
            }
        }
        Talk(1000);
        Assert.Equal([OVS.Client.Audio.Sounds.SoundEvent.LinkVoice], heard);
        f.Time.Advance(TimeSpan.FromMilliseconds(200)); // a short breath: still the same transmission
        Talk(300);
        Assert.Single(heard);
        f.Time.Advance(TimeSpan.FromMilliseconds(400)); // a real pause: a new transmission
        Talk(300);
        Assert.Equal(2, heard.Count);
    }

    /// <summary>Package 60: starting to talk over a link oneself gives an own tone, once per transmission.</summary>
    [Fact]
    public void OwnLinkVoice_OnStartOfOwnLinkTransmission()
    {
        var f = Create();
        f.Vm.Apply(new ChannelsLinked(Lobby, Bravo));
        var heard = Sounds(f.Vm);
        const byte Linked = OVS.Shared.Voice.VoiceHeader.TargetLinked;
        f.Vm.Tree.SetSelfTransmitting(Linked);
        f.Vm.Tree.SetSelfTransmitting(Linked); // still the same transmission
        Assert.Equal([OVS.Client.Audio.Sounds.SoundEvent.OwnLinkVoice], heard);

        f.Vm.Tree.SetSelfTransmitting(null);
        f.Time.Advance(TimeSpan.FromMilliseconds(100)); // let go and pressed again at once
        f.Vm.Tree.SetSelfTransmitting(Linked);
        Assert.Single(heard);

        f.Vm.Tree.SetSelfTransmitting(null);
        f.Time.Advance(TimeSpan.FromMilliseconds(400));
        f.Vm.Tree.SetSelfTransmitting(Linked);
        Assert.Equal(2, heard.Count);
    }

    [Fact]
    public void OwnLinkVoice_NotWithoutLinksOrForChannelOnly()
    {
        var f = Create();
        var heard = Sounds(f.Vm);
        f.Vm.Tree.SetSelfTransmitting(OVS.Shared.Voice.VoiceHeader.TargetLinked); // no link from my channel: nobody else hears it
        f.Vm.Tree.SetSelfTransmitting(null);
        f.Vm.Apply(new ChannelsLinked(Lobby, Bravo));
        f.Time.Advance(TimeSpan.FromSeconds(1));
        f.Vm.Tree.SetSelfTransmitting(OVS.Shared.Voice.VoiceHeader.TargetChannel); // only the own channel, e.g. without the right
        Assert.Empty(heard);
    }

    [Fact]
    public void LinkVoice_NotForOwnChannelOrSelf()
    {
        var f = Create(others: [User(2, "anna", Lobby, P.Speak)]);
        var heard = Sounds(f.Vm);
        f.Vm.Tree.OnSpeakers([new ActiveSpeaker(2, ViaLink: false)]); // in my channel
        f.Vm.Tree.OnSpeakers([new ActiveSpeaker(1, ViaLink: true)]); // myself
        f.Vm.Tree.OnSpeakers([new ActiveSpeaker(OVS.Client.Audio.AudioEngine.SelfTestSpeaker, ViaLink: true)]); // the self test
        Assert.Empty(heard);
    }

    /// <summary>Package 56: "Allgemein" and the channel chat have their own sound; nobody hears their own messages.</summary>
    [Fact]
    public void ChatMessages_SoundPerTarget_NotForOwn()
    {
        var f = Create(others: [User(2, "anna", Lobby, P.Speak)]);
        var heard = Sounds(f.Vm);
        f.Vm.Apply(new ChatMessage(ChatTarget.Server, 2, "anna", null, null, "Hallo alle", DateTimeOffset.UtcNow));
        f.Vm.Apply(new ChatMessage(ChatTarget.Channel, 2, "anna", Lobby, null, "Hallo Lobby", DateTimeOffset.UtcNow));
        f.Vm.Apply(new ChatMessage(ChatTarget.Private, 2, "anna", null, 1, "psst", DateTimeOffset.UtcNow));
        f.Vm.Apply(new ChatMessage(ChatTarget.Server, 1, "ich", null, null, "selbst", DateTimeOffset.UtcNow));
        f.Vm.Apply(new ChatMessage(ChatTarget.Channel, 1, "ich", Lobby, null, "selbst", DateTimeOffset.UtcNow));
        Assert.Equal([OVS.Client.Audio.Sounds.SoundEvent.ServerMessage, OVS.Client.Audio.Sounds.SoundEvent.ChannelMessage, OVS.Client.Audio.Sounds.SoundEvent.PrivateMessage], heard);
    }

    [Fact]
    public async Task SoundEvents_OwnChanges_NotDoubled()
    {
        var f = Create();
        var heard = Sounds(f.Vm);
        await f.Vm.SelfState.ToggleMuteCommand.ExecuteAsync(null);
        await f.Vm.SelfState.ToggleMuteCommand.ExecuteAsync(null);
        await f.Vm.SelfState.ToggleDeafenCommand.ExecuteAsync(null);
        await f.Vm.SelfState.ToggleMuteCommand.ExecuteAsync(null); // unmuting while deafened turns the sound back on
        await f.Vm.JoinAsync(Bravo);
        f.Vm.Apply(new UserUpdated(User(1, "ich", Bravo, P.Speak) with { SelfMuted = false }));
        f.Vm.Apply(new UserUpdated(User(1, "ich", Bravo, P.Speak) with { SelfMuted = true })); // the server's echo of the own mute: no second tone
        Assert.Equal(
        [
            OVS.Client.Audio.Sounds.SoundEvent.MicOff, OVS.Client.Audio.Sounds.SoundEvent.MicOn, OVS.Client.Audio.Sounds.SoundEvent.SoundOff,
            OVS.Client.Audio.Sounds.SoundEvent.SoundOn, OVS.Client.Audio.Sounds.SoundEvent.ChannelEntered,
        ], heard);
    }

    [Fact]
    public void SlotText_LimitedAndUnlimited()
    {
        var f = Create(others: [User(2, "anna", Bravo, P.Speak)]);
        Assert.Equal("1", f.Channel(Bravo).SlotText);
        f.Vm.Apply(new ChannelUpdated(new ChannelInfo(Bravo, "Bravo", "", 1, MaxUsers: 5)));
        Assert.Equal("1/5", f.Channel(Bravo).SlotText);
        Assert.Equal(5, f.Channel(Bravo).MaxUsers);
    }

    [Fact]
    public void SlotText_UserJoins_ChangeRaised()
    {
        var f = Create(others: [User(2, "anna", Bravo, P.Speak)]);
        var changed = new List<string?>();
        f.Channel(Bravo).PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        f.Vm.Apply(new UserJoined(User(3, "bert", Bravo, P.Speak)));
        Assert.Equal("2", f.Channel(Bravo).SlotText);
        Assert.Contains(nameof(ChannelViewModel.SlotText), changed); // the counter's text follows
    }

    [Fact]
    public void MaxUsers_LimitChanges_ChangeRaised()
    {
        var f = Create(others: [User(2, "anna", Bravo, P.Speak)]);
        var changed = new List<string?>();
        f.Channel(Bravo).PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        f.Vm.Apply(new ChannelUpdated(new ChannelInfo(Bravo, "Bravo", "", 1, MaxUsers: 5)));
        Assert.Equal(5, f.Channel(Bravo).MaxUsers);
        Assert.Contains(nameof(ChannelViewModel.MaxUsers), changed);
    }

    [Fact]
    public void RedeemToken_HiddenForAdmin()
    {
        Assert.True(Create().Vm.CanRedeemToken);
        Assert.False(Create(P.All, [WellKnownGroups.Admin]).Vm.CanRedeemToken);
    }

    [Fact]
    public async Task Ban_SendsDurationAndIpFlag()
    {
        var dialogs = new ServerDialogs { Ban = (_, _, _) => Task.FromResult<BanChoice?>(new BanChoice("spam", 60, true)) };
        var f = Create(Moderator, dialogs: dialogs, others: User(2, "gast", Lobby, P.Speak));
        await f.User(2).BanCommand.ExecuteAsync(null);
        Assert.Equal(new BanSession(2, "spam", 60, true), f.Sent[^1] with { RequestId = null });
    }

    [Fact]
    public async Task CancelledDialog_SendsNothing()
    {
        var f = Create(P.All, others: User(2, "gast", Lobby, P.Speak));
        await f.User(2).KickCommand.ExecuteAsync(null);
        await f.Channel(Alpha).DeleteCommand.ExecuteAsync(null);
        await f.Vm.NewChannelCommand.ExecuteAsync(null);
        Assert.Empty(f.Sent);
    }

    // ---- Package 98 (A113): waiting states ----

    /// <summary>Package 98 (AC1): the target row spins until the own channel changes or an error comes; a second join meanwhile is ignored.</summary>
    [Fact]
    public async Task JoinChannel_RowBusyUntilMoved_SecondJoinIgnored()
    {
        var f = Create();
        var notices = new List<string>();
        f.Vm.Notice += notices.Add;
        var bravo = f.Channel(Bravo);
        await bravo.JoinCommand.ExecuteAsync(null);
        f.Time.Advance(PendingActionViewModel.ShowAfter - TimeSpan.FromMilliseconds(1));
        Assert.False(bravo.IsBusy); // a fast answer never flickers
        f.Time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(bravo.IsBusy);
        await f.Channel(Alpha).JoinCommand.ExecuteAsync(null);
        Assert.Single(f.Sent.OfType<JoinChannel>());
        f.Vm.Apply(new UserUpdated(User(1, "ich", Bravo, P.Speak)));
        Assert.False(bravo.IsBusy);
        Assert.True(bravo.IsCurrent);

        // refused: the spinner goes and the reason shows; joining works again
        var alpha = f.Channel(Alpha);
        await alpha.JoinCommand.ExecuteAsync(null);
        f.Time.Advance(PendingActionViewModel.ShowAfter);
        Assert.True(alpha.IsBusy);
        f.Vm.Apply(new Error(f.Sent[^1].RequestId, ReasonCodes.ChannelFull));
        Assert.False(alpha.IsBusy);
        Assert.Equal(ErrorTexts.For(ReasonCodes.ChannelFull), notices[^1]);

        // no answer at all: a visible error after 10 s, then it can be tried again
        await alpha.JoinCommand.ExecuteAsync(null);
        f.Time.Advance(TimeSpan.FromSeconds(10));
        Assert.False(alpha.IsBusy);
        Assert.Equal(OVS.Client.Localization.Strings.Pending_NoAnswer, notices[^1]);
        await alpha.JoinCommand.ExecuteAsync(null);
        Assert.Equal(4, f.Sent.OfType<JoinChannel>().Count());
    }

    /// <summary>Package 98 (AC4): a dropped channel stays in its new place until the server confirms; refused, it goes back.</summary>
    [Fact]
    public async Task Reorder_RevertsOnError()
    {
        var f = Create(P.ChannelEdit);
        Guid[] Order() => f.Vm.Tree.Channels.Select(c => c.Id).ToArray();
        Assert.Equal([Lobby, Alpha, Bravo], Order());

        await f.Vm.Tree.MoveChannelAsync(f.Channel(Bravo), f.Channel(Lobby), after: false);
        var refused = Assert.IsType<ReorderChannels>(f.Sent[^1]);
        Assert.Equal([Bravo, Lobby, Alpha], Order()); // at once, no jump back
        f.Vm.Apply(new UserJoined(User(2, "anna", Lobby, P.Speak))); // something else changes meanwhile: the new order stays
        Assert.Equal([Bravo, Lobby, Alpha], Order());
        f.Time.Advance(PendingActionViewModel.ShowAfter);
        Assert.True(f.Channel(Bravo).IsBusy);
        f.Vm.Apply(new Error(refused.RequestId, ReasonCodes.PermissionDenied));
        Assert.Equal([Lobby, Alpha, Bravo], Order());
        Assert.False(f.Channel(Bravo).IsBusy);

        // accepted: the server's updates confirm the order
        await f.Vm.Tree.MoveChannelAsync(f.Channel(Bravo), f.Channel(Lobby), after: false);
        f.Time.Advance(PendingActionViewModel.ShowAfter);
        f.Vm.Apply(new ChannelUpdated(new ChannelInfo(Bravo, "Bravo", "", 0)));
        f.Vm.Apply(new ChannelUpdated(new ChannelInfo(Lobby, "Lobby", "", 1)));
        f.Vm.Apply(new ChannelUpdated(new ChannelInfo(Alpha, "alpha", "", 2)));
        Assert.Equal([Bravo, Lobby, Alpha], Order());
        Assert.False(f.Channel(Bravo).IsBusy);
        f.Time.Advance(TimeSpan.FromSeconds(10)); // no late timeout puts anything back
        Assert.Equal([Bravo, Lobby, Alpha], Order());

        // no answer: back after 10 s
        await f.Vm.Tree.MoveChannelAsync(f.Channel(Alpha), f.Channel(Bravo), after: false);
        Assert.Equal([Alpha, Bravo, Lobby], Order());
        f.Time.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal([Bravo, Lobby, Alpha], Order());
    }

    /// <summary>Package 98 (AC3, AC4): a sending dialog gets the send as its submit and waits; the tree's actions spin on their row.</summary>
    [Fact]
    public async Task SendingDialogs_WaitForTheConfirmation_RowActionsSpin()
    {
        PendingActionViewModel? waited = null;
        var dialogs = new ServerDialogs
        {
            EditChannel = (current, _, submit) =>
            {
                waited = submit!(current with { Name = "Neu" });
                return Task.FromResult<ChannelEdit?>(current with { Name = "Neu" });
            },
            Confirm = _ => Task.FromResult(true),
        };
        var f = Create(Moderator | P.ChannelCreate | P.ChannelDelete, dialogs: dialogs, others: User(2, "gast", Lobby, P.Speak));
        await f.Vm.NewChannelCommand.ExecuteAsync(null);
        Assert.Single(f.Sent.OfType<CreateChannel>()); // sent once, by the dialog's submit
        Assert.True(waited!.IsRunning);
        var created = Guid.NewGuid();
        f.Vm.Apply(new ChannelAdded(new ChannelInfo(Guid.NewGuid(), "Neu", "", 4))); // Package 110: someone else's "Neu"
        Assert.True(waited.IsRunning);
        f.Vm.Apply(new ChannelAdded(new ChannelInfo(created, "Neu", "", 3), f.Sent.OfType<CreateChannel>().Single().RequestId));
        Assert.False(waited.IsRunning);
        Assert.Null(await waited.Completion);

        // the mute toggle spins on the user's row until the server's update
        var gast = f.User(2);
        await gast.ToggleServerMuteCommand.ExecuteAsync(null);
        f.Time.Advance(PendingActionViewModel.ShowAfter);
        Assert.True(gast.IsBusy);
        f.Vm.Apply(new UserUpdated(User(2, "gast", Lobby, P.Speak) with { ServerMuted = true }));
        Assert.False(gast.IsBusy);

        // deleting a channel spins on its row until it is gone
        var channel = f.Channel(created);
        await channel.DeleteCommand.ExecuteAsync(null);
        f.Time.Advance(PendingActionViewModel.ShowAfter);
        Assert.True(channel.IsBusy);
        f.Vm.Apply(new ChannelRemoved(created));
        Assert.False(channel.IsBusy);
    }

    /// <summary>Package 102: a user or channel folding away is no target, no count and no neighbour any more.</summary>
    [Fact]
    public void LeavingUser_NotCountedNotTarget_LeavingChannelSkipped()
    {
        var clock = new ManualTimeProvider();
        var sent = new List<Request>();
        var server = FakeServers.Admin(sent, time: clock);
        server.Post = a => a();
        server.Leave.Delay = TimeSpan.FromMinutes(1);
        var raid = server.Tree.Channels.Single(c => c.Name == "Raid");
        server.Apply(new UserLeft(2));
        Assert.Contains(raid.Users, u => u.SessionId == 2); // still shown, folding away
        Assert.True(CollectionSync.IsLeaving(raid.Users, raid.Users.Single()));
        Assert.Equal("0", raid.SlotText);

        var archiv = Guid.NewGuid();
        server.Apply(new ChannelAdded(new ChannelInfo(archiv, "Archiv", "", 5)));
        server.Apply(new ChannelRemoved(FakeServers.Raid));
        Assert.Contains(server.Tree.Channels, c => c.Name == "Raid");
        Assert.Equal(["Lobby", "Archiv"], server.Tree.Channels.Live().Select(c => c.Name));
        var lobby = server.Tree.Channels.Single(c => c.Name == "Lobby");
        Assert.DoesNotContain(server.Tree.LinkCandidates(lobby), c => c.Name == "Raid");

        // Lobby's neighbour below is Archiv, not the folding Raid
        lobby.MoveDownCommand.Execute(null);
        Assert.Equal([archiv, FakeServers.Lobby], sent.OfType<ReorderChannels>().Single().ChannelIds);
    }

    /// <summary>Package 102: someone who comes back while folding away is the same entry again.</summary>
    [Fact]
    public void UserBackWhileLeaving_SameEntry()
    {
        var clock = new ManualTimeProvider();
        var server = FakeServers.Admin(time: clock);
        server.Post = a => a();
        server.Leave.Delay = TimeSpan.FromMinutes(1);
        var raid = server.Tree.Channels.Single(c => c.Name == "Raid");
        var anna = raid.Users.Single();
        server.Apply(new UserLeft(2));
        server.Apply(new UserJoined(new UserInfo(2, "fp2", "anna", FakeServers.Raid, false, false, false, Permission.None, [WellKnownGroups.Guest])));
        Assert.Same(anna, raid.Users.Single());
        Assert.False(CollectionSync.IsLeaving(raid.Users, anna));
    }

    /// <summary>Package 102: moving next to a channel that folds away does nothing.</summary>
    [Fact]
    public async Task MoveChannel_BesideFoldingChannel_DoesNothing()
    {
        var sent = new List<Request>();
        var server = FakeServers.Admin(sent, time: new ManualTimeProvider());
        server.Post = a => a();
        server.Leave.Delay = TimeSpan.FromMinutes(1);
        var lobby = server.Tree.Channels.Single(c => c.Name == "Lobby");
        var raid = server.Tree.Channels.Single(c => c.Name == "Raid");
        server.Apply(new ChannelRemoved(FakeServers.Raid));
        await server.Tree.MoveChannelAsync(lobby, raid, after: false);
        await server.Tree.MoveChannelAsync(lobby, raid, after: true);
        await server.Tree.MoveChannelAsync(raid, lobby, after: true);
        Assert.Empty(sent.OfType<ReorderChannels>());
    }

    /// <summary>Package 111: a separator cannot be joined, edited or linked and is no target; it can still be moved and deleted.</summary>
    [Fact]
    public async Task Separator_NoJoinNoEditNoLink_NotAMoveTarget()
    {
        var f = Create(P.All, others: User(2, "anna", Lobby, P.Speak) with { CanBeModeratedByMe = true });
        var id = Guid.NewGuid();
        f.Vm.Apply(new ChannelAdded(new ChannelInfo(id, "", "", 5, Kind: ChannelKind.Separator)));
        var separator = f.Channel(id);
        Assert.True(separator.IsSeparator);
        Assert.False(separator.IsVoice);
        Assert.Equal((false, false, false), (separator.CanJoin, separator.CanEditDetails, separator.CanLink));
        Assert.Equal((true, true), (separator.CanEdit, separator.CanDelete)); // moves and goes like a channel
        Assert.Null(separator.Tooltip);
        Assert.Equal("0", separator.SlotText);

        await separator.JoinCommand.ExecuteAsync(null); // also the double click
        Assert.Empty(f.Sent.OfType<JoinChannel>());
        Assert.DoesNotContain(separator, f.Vm.Tree.LinkCandidates(f.Channel(Lobby)));
        Assert.NotEmpty(f.User(2).MoveTargets);
        Assert.DoesNotContain(separator, f.User(2).MoveTargets.Select(t => t.Channel)); // Package 120: the submenu
    }

    /// <summary>Package 112: without description and links there is no tooltip at all (an empty text showed an empty box).</summary>
    [Fact]
    public void Tooltip_NoDescriptionNoLinks_IsNull()
    {
        var f = Create();
        Assert.Null(f.Channel(Bravo).Tooltip);
        f.Vm.Apply(new ChannelUpdated(new ChannelInfo(Bravo, "Bravo", "   ", 1)));
        Assert.Null(f.Channel(Bravo).Tooltip); // only spaces count as none
        // the same for a user row: in no known group, no tooltip
        var g = Create(others: [User(2, "anna", Lobby, P.Speak, groups: [])]);
        Assert.Null(g.User(2).GroupTip);
        Assert.Equal("Gast", g.User(1).GroupTip);
    }

    [Fact]
    public void Tooltip_DescriptionOrLinks_Unchanged()
    {
        var f = Create();
        f.Vm.Apply(new ChannelUpdated(new ChannelInfo(Bravo, "Bravo", "Nur Raid", 1)));
        Assert.Equal("Nur Raid", f.Channel(Bravo).Tooltip);
        f.Vm.Apply(new ChannelsLinked(Lobby, Alpha));
        Assert.Equal(string.Format(OVS.Client.Localization.Strings.Tooltip_LinkedWith, "Lobby"), f.Channel(Alpha).Tooltip);
        f.Vm.Apply(new ChannelsLinked(Bravo, Alpha));
        Assert.Equal("Nur Raid" + (char)10 + string.Format(OVS.Client.Localization.Strings.Tooltip_LinkedWith, "alpha"), f.Channel(Bravo).Tooltip);
    }

    // ---- Package 119 (A128): groups from the user's context menu ----

    static readonly Guid Raid = Guid.NewGuid();

    /// <summary>Guest and Raid assignable by me, Admin not (stronger than me).</summary>
    static readonly GroupInfo[] AssignGroups =
        [new(WellKnownGroups.Guest, "Gast", P.Speak, true), new(WellKnownGroups.Admin, "Admin", P.All), new(Raid, "Raid", P.Speak, true)];

    static UserInfo Weaker(IReadOnlyList<Guid>? groups = null) => User(2, "anna", Lobby, P.Speak, groups: groups) with { CanBeModeratedByMe = true };

    static List<(Guid, string, bool, bool)> Ticks(UserViewModel user) => user.Groups.Select(t => (t.GroupId, t.Name, t.IsChecked, t.IsEnabled)).ToList();

    /// <summary>Package 119 (AC1, AC2): the submenu only for weaker users and only with GroupsAssign; a group I may not give is greyed out.</summary>
    [Fact]
    public void UserGroups_OnlyForWeakerUsersWithAssignRight()
    {
        var f = Create(Moderator | P.GroupsAssign, groups: AssignGroups, others: [Weaker(), User(3, "bert", Lobby, P.Speak)]);
        Assert.Empty(f.User(1).Groups); // myself
        Assert.Empty(f.User(3).Groups); // stronger or equal: the server does not let me act on him
        Assert.Equal([(WellKnownGroups.Guest, "Gast", true, true), (WellKnownGroups.Admin, "Admin", false, false), (Raid, "Raid", false, true)],
            Ticks(f.User(2)));

        var without = Create(Moderator, groups: AssignGroups, others: [Weaker()]);
        Assert.Empty(without.User(2).Groups); // no GroupsAssign
    }

    /// <summary>Package 119 (AC3, AC4): a click sends at once, the tick and tooltip follow the server; a refusal puts the tick back.</summary>
    [Fact]
    public async Task UserGroupToggle_SendsAssign_ConfirmedByUserUpdated()
    {
        var f = Create(Moderator | P.GroupsAssign, groups: AssignGroups, others: [Weaker()]);
        var notices = new List<string>();
        f.Vm.Notice += notices.Add;
        var anna = f.User(2);

        var raid = anna.Groups.Single(t => t.GroupId == Raid);
        raid.IsChecked = true; // the menu item ticks itself on the click, then runs the command
        await raid.ToggleCommand.ExecuteAsync(null);
        Assert.Equal(new AssignGroup("fp2", Raid), f.Sent[^1] with { RequestId = null });
        f.Time.Advance(PendingActionViewModel.ShowAfter);
        Assert.True(anna.IsBusy);
        f.Vm.Apply(new UserUpdated(Weaker([WellKnownGroups.Guest]) with { SelfMuted = true })); // another change meanwhile
        Assert.True(anna.Groups.Single(t => t.GroupId == Raid).IsChecked); // the tick stays until the server shows the group
        Assert.True(anna.IsBusy);
        f.Vm.Apply(new UserUpdated(Weaker([WellKnownGroups.Guest, Raid])));
        Assert.False(anna.IsBusy);
        Assert.True(anna.Groups.Single(t => t.GroupId == Raid).IsChecked);
        Assert.Equal("Gast, Raid", anna.GroupTip);

        // taken away again, but refused: the error shows and the tick goes back
        raid = anna.Groups.Single(t => t.GroupId == Raid);
        raid.IsChecked = false;
        await raid.ToggleCommand.ExecuteAsync(null);
        Assert.Equal(new UnassignGroup("fp2", Raid), f.Sent[^1] with { RequestId = null });
        f.Time.Advance(PendingActionViewModel.ShowAfter);
        Assert.True(anna.IsBusy);
        f.Vm.Apply(new Error(f.Sent[^1].RequestId, ReasonCodes.PermissionDenied));
        Assert.False(anna.IsBusy);
        Assert.Equal(ErrorTexts.For(ReasonCodes.PermissionDenied), notices[^1]);
        Assert.True(anna.Groups.Single(t => t.GroupId == Raid).IsChecked);

        // a change made elsewhere shows the next time the menu opens
        f.Vm.Apply(new UserUpdated(Weaker([Raid])));
        Assert.Equal([false, false, true], anna.Groups.Select(t => t.IsChecked));
    }

    /// <summary>Package 119 (review): on, then off before any answer; the first answer neither shows the tick nor ends the spinner.</summary>
    [Fact]
    public async Task UserGroupToggle_OnThenOff_LastClickWins()
    {
        var f = Create(Moderator | P.GroupsAssign, groups: AssignGroups, others: [Weaker()]);
        var anna = f.User(2);
        GroupAssignmentViewModel Raid_() => anna.Groups.Single(t => t.GroupId == Raid);
        var raid = Raid_();
        raid.IsChecked = true;
        await raid.ToggleCommand.ExecuteAsync(null);
        raid = Raid_();
        raid.IsChecked = false;
        await raid.ToggleCommand.ExecuteAsync(null);
        Assert.IsType<UnassignGroup>(f.Sent[^1]);
        f.Time.Advance(PendingActionViewModel.ShowAfter);
        Assert.True(anna.IsBusy);

        f.Vm.Apply(new UserUpdated(Weaker([WellKnownGroups.Guest, Raid]))); // confirms the first click only
        Assert.False(Raid_().IsChecked);
        Assert.True(anna.IsBusy);
        f.Vm.Apply(new UserUpdated(Weaker([WellKnownGroups.Guest])));
        Assert.False(Raid_().IsChecked);
        Assert.False(anna.IsBusy);
    }

    /// <summary>
    /// Package 119 (AC2): the last admin keeps the Admin group. An Admin-group member has every right, so the server never
    /// judges one weaker than me and the menu has no groups for him, the same rule that keeps the administration's toggles off.
    /// </summary>
    [Fact]
    public void LastAdmin_CannotBeUnticked()
    {
        var f = Create(Moderator | P.GroupsAssign, groups: AssignGroups, others: [User(2, "chef", Lobby, P.All, [WellKnownGroups.Admin])]);
        Assert.Empty(f.User(2).Groups);
    }

    // ---- Package 120 (A129): moving users from the menu and by drag and drop ----

    /// <summary>Package 120 (AC1): the other channels in tree order, no separator; a click moves at once, without the dialog.</summary>
    [Fact]
    public void MoveTargets_OtherChannelsWithoutSeparators()
    {
        bool picked = false;
        var dialogs = new ServerDialogs
        {
            PickChannel = (_, _, _) =>
            {
                picked = true;
                return Task.FromResult<ChannelViewModel?>(null);
            },
        };
        var f = Create(Moderator, dialogs: dialogs, others: [Weaker(), User(3, "bert", Lobby, P.Speak)]);
        f.Vm.Apply(new ChannelAdded(new ChannelInfo(Guid.NewGuid(), "", "", 5, Kind: ChannelKind.Separator)));
        var anna = f.User(2);
        Assert.Equal(["alpha", "Bravo"], anna.MoveTargets.Select(t => t.Channel.Name));
        Assert.True(anna.HasMoveTargets);
        Assert.Empty(f.User(1).MoveTargets); // myself: that is joining
        Assert.Empty(f.User(3).MoveTargets); // not weaker than me
        Assert.Empty(Create(P.Speak, others: [Weaker()]).User(2).MoveTargets); // without UserMove

        anna.MoveTargets.Single(t => t.Channel.Id == Bravo).Command.Execute(null);
        Assert.Equal(new MoveUser(2, Bravo), f.Sent[^1] with { RequestId = null });
        Assert.False(picked);
        f.Time.Advance(PendingActionViewModel.ShowAfter);
        Assert.True(anna.IsBusy); // Package 98: the row spins until the server shows the move
        f.Vm.Apply(new UserUpdated(Weaker() with { ChannelId = Bravo }));
        Assert.False(anna.IsBusy);
        Assert.Equal(["Lobby", "alpha"], anna.MoveTargets.Select(t => t.Channel.Name)); // follows the user
    }

    /// <summary>Package 120 (AC5): a refused move shows the server's error; the user stays where they were.</summary>
    [Fact]
    public async Task RefusedMove_ShowsError()
    {
        var f = Create(Moderator, others: [Weaker()]);
        var notices = new List<string>();
        f.Vm.Notice += notices.Add;
        var anna = f.User(2);
        Assert.True(anna.CanDrag);
        var done = f.Vm.DropUser(anna, f.Channel(Bravo));
        Assert.Equal(new MoveUser(2, Bravo), f.Sent[^1] with { RequestId = null });
        Assert.False(done.IsCompleted); // waits for the server
        f.Vm.Apply(new Error(f.Sent[^1].RequestId, ReasonCodes.ChannelFull));
        await done;
        Assert.Equal(ErrorTexts.For(ReasonCodes.ChannelFull), notices[^1]);
        Assert.Equal(Lobby, anna.ChannelId);
        Assert.Contains(anna, f.Channel(Lobby).Users);
        Assert.False(anna.IsBusy);
    }

    /// <summary>Package 120 (review): a user who left the server meanwhile is not moved by a drop of their row.</summary>
    [Fact]
    public void DropUser_LeftMeanwhile_SendsNothing()
    {
        var f = Create(Moderator, others: [Weaker()]);
        var anna = f.User(2);
        f.Vm.Apply(new UserLeft(2));
        Assert.True(f.Vm.DropUser(anna, f.Channel(Bravo)).IsCompleted); // nothing to wait for
        Assert.Empty(f.Sent);
    }

    /// <summary>Package 120 (AC3, AC4): the own row joins (with the password prompt); the own channel and a separator send nothing.</summary>
    [Fact]
    public async Task DropUser_OwnRowJoins_InvalidTargetsSendNothing()
    {
        var asked = new List<string>();
        var f = Create(Moderator, dialogs: new ServerDialogs { AskChannelPassword = name => { asked.Add(name); return Task.FromResult<string?>("geheim"); } },
            others: [Weaker(), User(3, "bert", Lobby, P.Speak)]);
        var separator = Guid.NewGuid();
        f.Vm.Apply(new ChannelAdded(new ChannelInfo(separator, "", "", 5, Kind: ChannelKind.Separator)));
        f.Vm.Apply(new ChannelUpdated(new ChannelInfo(Bravo, "Bravo", "", 1, HasPassword: true)));
        var (me, anna, bert) = (f.User(1), f.User(2), f.User(3));
        Assert.Equal((true, false), (me.CanDrag, bert.CanDrag));
        Assert.False(ServerViewModel.IsDropTarget(anna, f.Channel(Lobby))); // her own channel: the server would do nothing
        Assert.False(ServerViewModel.IsDropTarget(anna, f.Channel(separator)));
        Assert.False(ServerViewModel.IsDropTarget(bert, f.Channel(Bravo)));
        Assert.True(ServerViewModel.IsDropTarget(anna, f.Channel(Bravo)));
        await f.Vm.DropUser(anna, f.Channel(Lobby));
        await f.Vm.DropUser(bert, f.Channel(Bravo));
        await f.Vm.DropUser(me, f.Channel(separator));
        Assert.Empty(f.Sent);

        var joined = f.Vm.DropUser(me, f.Channel(Bravo));
        Assert.Equal(["Bravo"], asked);
        Assert.Equal(new JoinChannel(Bravo, "geheim"), f.Sent[^1] with { RequestId = null });
        f.Vm.Apply(new UserUpdated(User(1, "ich", Bravo, Moderator)));
        await joined;
        Assert.Equal(Bravo, f.Vm.CurrentChannel!.Id);
    }
}
