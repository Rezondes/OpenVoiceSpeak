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

    static Fixture Create(P selfPerms = P.Speak, IReadOnlyList<Guid>? selfGroups = null, ServerDialogs? dialogs = null, IReadOnlyList<GroupInfo>? groups = null,
        params UserInfo[] others)
    {
        var snapshot = Snapshot("Server", "Hallo", Lobby,
            [new ChannelInfo(Lobby, "Lobby", "", 0), new ChannelInfo(Bravo, "Bravo", "", 1), new ChannelInfo(Alpha, "alpha", "", 1)],
            groups ?? [new GroupInfo(WellKnownGroups.Guest, "Gast", P.Speak), new GroupInfo(WellKnownGroups.Admin, "Admin", P.All)],
            [User(1, "ich", Lobby, selfPerms, selfGroups), .. others]);
        var sent = new List<Request>();
        var time = new ManualTimeProvider();
        var server = new ServerViewModel(Mirror(snapshot), request =>
        {
            sent.Add(request);
            return Task.CompletedTask;
        }, time, dialogs);
        return new Fixture(server, sent, time);
    }

    [Fact]
    public void Build_Snapshot_SortsChannelsAndGroupsUsers()
    {
        var fixture = Create(others: [User(2, "zora", Lobby, P.Speak), User(3, "Anna", Lobby, P.Speak), User(4, "bob", Bravo, P.Speak)]);
        Assert.Equal(["Lobby", "alpha", "Bravo"], fixture.Vm.Tree.Channels.Select(channel => channel.Name));
        Assert.Equal(["Anna", "ich", "zora"], fixture.Channel(Lobby).Users.Select(user => user.Nickname));
        Assert.Equal(["bob"], fixture.Channel(Bravo).Users.Select(user => user.Nickname));
        Assert.True(fixture.Channel(Lobby).IsCurrent);
        Assert.True(fixture.User(1).IsSelf);
        Assert.Equal("Server", fixture.Vm.ServerName);
    }

    /// <summary>Package 51: the volume per person, its icon and tooltip; nobody adjusts their own voice.</summary>
    [Fact]
    public void UserVolume_ShownPerUser_NotForSelf()
    {
        var fixture = Create(others: [User(2, "anna", Lobby, P.Speak), User(3, "bert", Lobby, P.Speak), User(4, "carla", Lobby, P.Speak)]);
        var volumes = new Dictionary<string, float> { ["fp2"] = 1.5f, ["fp3"] = 0f };
        var changed = new List<(string, float)>();
        fixture.Vm.VolumeOf = fp => volumes.GetValueOrDefault(fp, 1f);
        fixture.Vm.VolumeChanged += (fp, volume) => changed.Add((fp, volume));
        fixture.Vm.Apply(new UserUpdated(User(4, "carla", Lobby, P.Speak))); // rebuild with the volumes known

        var (anna, bert, carla, me) = (fixture.User(2), fixture.User(3), fixture.User(4), fixture.User(1));
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
    public void CurrentChannelAndSelf_OwnUserMoves_Follow()
    {
        var fixture = Create();
        Assert.Equal(Lobby, fixture.Vm.CurrentChannel!.Id);
        Assert.Equal("ich", fixture.Vm.Self!.Nickname);

        fixture.Vm.Apply(new UserUpdated(User(1, "ich", Bravo, P.Speak)));
        Assert.Equal(Bravo, fixture.Vm.CurrentChannel!.Id);
        Assert.Same(fixture.User(1), fixture.Vm.Self);
    }

    [Fact]
    public void StatusIcons_PlainMute_OnlyThenMicOff()
    {
        var fixture = Create(others:
        [
            new UserInfo(2, "fp2", "stumm", Lobby, true, false, false, P.Speak, []),
            new UserInfo(3, "fp3", "taub", Lobby, true, true, false, P.Speak, []),
            new UserInfo(4, "fp4", "server", Lobby, true, false, true, P.Speak, []),
        ]);
        Assert.True(fixture.User(2).IsSelfMutedOnly);
        Assert.False(fixture.User(2).IsDeafened);
        Assert.False(fixture.User(3).IsSelfMutedOnly);
        Assert.True(fixture.User(3).IsDeafened);
        Assert.False(fixture.User(4).IsSelfMutedOnly);
        Assert.True(fixture.User(4).ServerMuted);
    }

    [Fact]
    public void UserUpdated_MovesUser_KeepsInstance()
    {
        var fixture = Create(others: User(2, "anna", Lobby, P.Speak));
        var anna = fixture.User(2);
        fixture.Vm.Apply(new UserUpdated(User(2, "anna", Alpha, P.Speak)));
        Assert.DoesNotContain(anna, fixture.Channel(Lobby).Users);
        Assert.Same(anna, Assert.Single(fixture.Channel(Alpha).Users));
    }

    [Fact]
    public void Tree_UserJoinedAndLeft_Updated()
    {
        var fixture = Create();
        fixture.Vm.Apply(new UserJoined(User(5, "neu", Bravo, P.Speak)));
        Assert.Single(fixture.Channel(Bravo).Users);
        fixture.Vm.Apply(new UserLeft(5));
        Assert.Empty(fixture.Channel(Bravo).Users);
    }

    [Fact]
    public void Speaking_NoFrameFor300ms_Cleared()
    {
        var fixture = Create(others: User(2, "anna", Lobby, P.Speak));
        fixture.Vm.Tree.OnSpeakers([new ActiveSpeaker(2, false)]);
        Assert.True(fixture.User(2).IsSpeaking);

        fixture.Time.Advance(TimeSpan.FromMilliseconds(200));
        fixture.Vm.Tree.RefreshSpeaking();
        Assert.True(fixture.User(2).IsSpeaking);

        fixture.Time.Advance(TimeSpan.FromMilliseconds(150));
        fixture.Vm.Tree.RefreshSpeaking();
        Assert.False(fixture.User(2).IsSpeaking);
    }

    [Fact]
    public void Speaking_ViaLink_Flagged()
    {
        var fixture = Create(others: User(2, "anna", Alpha, P.Speak));
        fixture.Vm.Tree.OnSpeakers([new ActiveSpeaker(2, true)]);
        Assert.True(fixture.User(2).IsSpeakingViaLink);
        Assert.False(fixture.User(2).IsSpeaking);
    }

    [Fact]
    public void SelfTransmitting_Talking_ShowsOnOwnEntry()
    {
        var fixture = Create();
        fixture.Vm.Tree.SetSelfTransmitting(VoiceHeader.TargetLinked);
        Assert.True(fixture.User(1).IsSpeakingViaLink);
        fixture.Vm.Tree.SetSelfTransmitting(null);
        Assert.False(fixture.User(1).IsSpeakingViaLink);
    }

    [Fact]
    public void LinkedChannels_Linked_ShowPartners()
    {
        var fixture = Create();
        fixture.Vm.Apply(new ChannelsLinked(Lobby, Alpha));
        fixture.Vm.Apply(new ChannelsLinked(Lobby, Bravo));
        Assert.True(fixture.Channel(Lobby).IsLinked);
        Assert.Equal("alpha, Bravo", fixture.Channel(Lobby).LinkedNames);
        Assert.Equal("Lobby", fixture.Channel(Alpha).LinkedNames);
    }

    [Fact]
    public async Task JoinCommand_Executed_SendsJoinChannel()
    {
        var fixture = Create();
        await fixture.Channel(Alpha).JoinCommand.ExecuteAsync(null);
        var join = Assert.IsType<JoinChannel>(Assert.Single(fixture.Sent));
        Assert.Equal(Alpha, join.ChannelId);
        Assert.NotNull(join.RequestId);
    }

    [Fact]
    public async Task Deafen_Toggled_SendsSelfState()
    {
        var fixture = Create();
        await fixture.Vm.SelfState.ToggleDeafenCommand.ExecuteAsync(null);
        Assert.Equal(new SetSelfState(true, true), fixture.Sent[^1] with { RequestId = null });
        await fixture.Vm.SelfState.ToggleDeafenCommand.ExecuteAsync(null);
        Assert.Equal(new SetSelfState(false, false), fixture.Sent[^1] with { RequestId = null });
    }

    [Fact]
    public async Task Undeafen_MutedBefore_StaysMuted()
    {
        var fixture = Create();
        await fixture.Vm.SelfState.ToggleMuteCommand.ExecuteAsync(null);
        await fixture.Vm.SelfState.ToggleDeafenCommand.ExecuteAsync(null);
        await fixture.Vm.SelfState.ToggleDeafenCommand.ExecuteAsync(null);
        Assert.True(fixture.Vm.SelfState.SelfMuted); // was muted before deafening
        Assert.False(fixture.Vm.SelfState.SelfDeafened);
    }

    /// <summary>Package 93: the lock icon names the groups; joining is only offered with one of them or as admin.</summary>
    [Fact]
    public async Task LockedChannel_GroupsAndRights_IconTooltipAndJoinFollow()
    {
        var raid = Guid.NewGuid();
        var fixture = Create();
        string? notice = null;
        fixture.Vm.Notice += name => notice = name;
        fixture.Vm.Apply(new GroupsChanged([new(WellKnownGroups.Guest, "Gast", P.None), new(raid, "Raid", P.None), new(WellKnownGroups.Admin, "Admin", P.None)]));
        fixture.Vm.Apply(new ChannelUpdated(new ChannelInfo(Bravo, "Bravo", "", 1, AllowedGroupIds: [raid, WellKnownGroups.Admin])));
        var bravo = fixture.Channel(Bravo);
        Assert.Equal((true, false), (bravo.IsLocked, bravo.CanJoin));
        Assert.Equal("Nur für Gruppen: Raid, Admin", bravo.LockText);
        Assert.Equal((false, true), (fixture.Channel(Alpha).IsLocked, fixture.Channel(Alpha).CanJoin));

        await bravo.JoinCommand.ExecuteAsync(null);
        Assert.Empty(fixture.Sent);
        Assert.Equal(ErrorTexts.For(ReasonCodes.ChannelLocked), notice);

        fixture.Vm.Apply(new UserUpdated(User(1, "ich", Lobby, P.Speak, [WellKnownGroups.Guest, raid])));
        Assert.True(bravo.CanJoin);
        await bravo.JoinCommand.ExecuteAsync(null);
        Assert.Equal(new JoinChannel(Bravo), fixture.Sent[^1] with { RequestId = null });

        fixture.Vm.Apply(new ChannelUpdated(new ChannelInfo(Bravo, "Bravo", "", 1, AllowedGroupIds: [])));
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

        await HangGuard.BoundAsync(fixture.Channel(Bravo).JoinCommand.ExecuteAsync(null));

        Assert.Empty(fixture.Sent);
    }

    [Fact]
    public async Task JoinAsync_EmptyPasswordEntered_SendsNothing()
    {
        var fixture = Create(dialogs: new ServerDialogs { AskChannelPassword = _ => Task.FromResult<string?>("") });
        fixture.Vm.Apply(new ChannelUpdated(new ChannelInfo(Bravo, "Bravo", "", 1, HasPassword: true)));

        await HangGuard.BoundAsync(fixture.Vm.JoinAsync(Bravo));

        Assert.Empty(fixture.Sent);
    }

    [Fact]
    public async Task Join_ChannelPasswordDialogThrows_PropagatesAndSendsNothing()
    {
        var fixture = Create(dialogs: new ServerDialogs { AskChannelPassword = _ => throw new InvalidOperationException("Dialog kaputt") });
        fixture.Vm.Apply(new ChannelUpdated(new ChannelInfo(Bravo, "Bravo", "", 1, HasPassword: true)));

        await Assert.ThrowsAsync<InvalidOperationException>(() => HangGuard.BoundAsync(fixture.Channel(Bravo).JoinCommand.ExecuteAsync(null)));

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
        var fixture = Create(dialogs: dialogs);
        fixture.Vm.Apply(new ChannelUpdated(new ChannelInfo(Bravo, "Bravo", "", 1, HasPassword: true)));
        var bravo = fixture.Channel(Bravo);
        Assert.Equal((true, true, "Passwort"), (bravo.IsLocked, bravo.CanJoin, bravo.LockText));

        await bravo.JoinCommand.ExecuteAsync(null); // cancelled
        Assert.Empty(fixture.Sent);
        answer = "falsch";
        await bravo.JoinCommand.ExecuteAsync(null);
        Assert.Equal(new JoinChannel(Bravo, "falsch"), fixture.Sent[^1] with { RequestId = null });
        fixture.Vm.Apply(new Error(fixture.Sent[^1].RequestId, ReasonCodes.WrongChannelPassword));
        answer = "geheim";
        await bravo.JoinCommand.ExecuteAsync(null); // asked again
        Assert.Equal(new JoinChannel(Bravo, "geheim"), fixture.Sent[^1] with { RequestId = null });
        fixture.Vm.Apply(new UserUpdated(User(1, "ich", Bravo, P.Speak)));
        Assert.Equal(["Bravo", "Bravo", "Bravo"], asked);

        await fixture.Channel(Lobby).JoinCommand.ExecuteAsync(null);
        Assert.Equal(new JoinChannel(Lobby), fixture.Sent[^1] with { RequestId = null });
        fixture.Vm.Apply(new UserUpdated(User(1, "ich", Lobby, P.Speak)));
        await bravo.JoinCommand.ExecuteAsync(null); // remembered
        Assert.Equal(new JoinChannel(Bravo, "geheim"), fixture.Sent[^1] with { RequestId = null });
        Assert.Equal(3, asked.Count);

        foreach (var (perms, groups) in new (P, Guid)[] { (P.Speak | P.ChannelPasswordBypass, WellKnownGroups.Guest), (P.All, WellKnownGroups.Admin) })
        {
            var other = Create(perms, [groups], dialogs);
            other.Vm.Apply(new ChannelUpdated(new ChannelInfo(Bravo, "Bravo", "", 1, HasPassword: true)));
            await other.Channel(Bravo).JoinCommand.ExecuteAsync(null);
            Assert.Equal(new JoinChannel(Bravo), other.Sent[^1] with { RequestId = null });
        }
        Assert.Equal(3, asked.Count);

        fixture.Vm.Apply(new ChannelUpdated(new ChannelInfo(Bravo, "Bravo", "", 1, AllowedGroupIds: [WellKnownGroups.Guest], HasPassword: true)));
        Assert.Equal("Nur für Gruppen: Gast\nPasswort", bravo.LockText);
    }

    [Fact]
    public void Error_Received_BecomesGermanNotice()
    {
        var fixture = Create();
        string? notice = null;
        fixture.Vm.Notice += name => notice = name;
        fixture.Vm.Apply(new Error("r1", ReasonCodes.PermissionDenied));
        Assert.Equal(ErrorTexts.For(ReasonCodes.PermissionDenied), notice);
    }

    [Theory]
    [InlineData(P.Speak, false, false, false)]
    [InlineData(P.ChannelEdit, true, false, false)]
    [InlineData(P.ChannelDelete, false, true, false)]
    [InlineData(P.ChannelLink, false, false, true)]
    [InlineData(P.All, true, true, true)]
    public void ChannelMenu_Rights_VisibleByPermission(P perms, bool edit, bool delete, bool link)
    {
        var fixture = Create(perms);
        var alpha = fixture.Channel(Alpha);
        Assert.Equal((edit, delete, link), (alpha.CanEdit, alpha.CanDelete, alpha.CanLink));
        Assert.Equal(perms.Has(P.ChannelCreate), alpha.CanCreate);
        Assert.False(fixture.Channel(Lobby).CanDelete); // default channel never
        Assert.False(alpha.CanUnlink);
        Assert.Equal(perms.Has(P.ChannelCreate), fixture.Vm.CanCreateChannel);
    }

    [Fact]
    public void UserMenu_RightsAndRank_VisibleAccordingly()
    {
        // Package 92: the rank comes from the server as a flag, others' rights are never sent
        var fixture = Create(Moderator, others: [User(2, "gast", Lobby, P.None) with { CanBeModeratedByMe = true }, User(3, "admin", Lobby, P.None)]);
        var guest = fixture.User(2);
        Assert.True(guest.CanKick && guest.CanBan && guest.CanMute && guest.CanMove);

        var admin = fixture.User(3);
        Assert.False(admin.CanKick || admin.CanBan || admin.CanMute || admin.CanMove);

        var self = fixture.User(1);
        Assert.False(self.CanKick || self.CanBan || self.CanMute || self.CanMove); // Package 84: moving oneself is joining

        var plain = Create(others: User(2, "gast", Lobby, P.None) with { CanBeModeratedByMe = true }); // weaker, but no right to act
        Assert.False(plain.User(2).CanKick || plain.User(2).CanMove);
    }

    /// <summary>
    /// Package 84 (A102): the menu offers exactly what the server allows, by the same rule.
    /// Package 92: the server sends the rule's result per recipient (StateSyncTests); here the rights become that flag.
    /// </summary>
    [Fact]
    public void UserActions_ServerRule_MatchFlag()
    {
        var others = new[]
        {
            User(2, "gast", Lobby, P.Speak), User(3, "mod2", Lobby, Moderator), User(4, "admin", Lobby, P.All),
            User(5, "zweit", Lobby, P.Speak) with { Fingerprint = "fp1" }, // another session of the same identity
            User(6, "anders", Lobby, P.Speak | P.ChatServer), // neither contains the other
        };
        var fixture = Create(Moderator, others: others.Select(user => user with
        {
            Permissions = P.None, CanBeModeratedByMe = Moderator.CanModerate("fp1", user.Permissions, user.Fingerprint),
        }).ToArray());
        foreach (var user in others)
        {
            var allowed = Moderator.CanModerate("fp1", user.Permissions, user.Fingerprint);
            var viewModel = fixture.User(user.SessionId);
            Assert.Equal([allowed, allowed, allowed, allowed], [viewModel.CanKick, viewModel.CanBan, viewModel.CanMute, viewModel.CanMove]);
        }
        Assert.True(fixture.User(2).CanKick);
        Assert.False(fixture.User(3).CanKick || fixture.User(3).CanBan || fixture.User(3).CanMute || fixture.User(3).CanMove);
        Assert.False(fixture.User(5).CanKick);
    }

    [Fact]
    public void LinkCandidates_SelfAndLinked_Excluded()
    {
        var fixture = Create(P.All);
        fixture.Vm.Apply(new ChannelsLinked(Lobby, Alpha));
        Assert.Equal([Bravo], fixture.Vm.Tree.LinkCandidates(fixture.Channel(Lobby)).Select(channel => channel.Id));
        Assert.Equal([Alpha], fixture.Vm.Tree.LinkedChannelVms(fixture.Channel(Lobby)).Select(channel => channel.Id));
        Assert.True(fixture.Channel(Lobby).CanUnlink);
    }

    [Fact]
    public async Task LinkCommand_ChannelPicked_UsesIt()
    {
        var dialogs = new ServerDialogs { PickChannel = (_, candidates, _) => Task.FromResult<ChannelViewModel?>(candidates.Single(channel => channel.Name == "Bravo")) };
        var fixture = Create(P.All, dialogs: dialogs);
        await fixture.Channel(Lobby).LinkCommand.ExecuteAsync(null);
        Assert.Equal(new LinkChannels(Lobby, Bravo), fixture.Sent[^1] with { RequestId = null });
    }

    /// <summary>Package 54: creating opens the same dialog, empty, and sends every option.</summary>
    [Fact]
    public async Task NewChannel_DialogConfirmed_SendsAllOptions()
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
        var fixture = Create(P.All, dialogs: dialogs);
        await fixture.Channel(Alpha).CreateCommand.ExecuteAsync(null);
        Assert.Equal((new ChannelEdit("", ""), ChannelDialogMode.Create), shown);
        Assert.Equal(new CreateChannel("Neu", "Beschreibung", IsMuted: true, MaxUsers: 3), fixture.Sent[^1] with { RequestId = null });
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
        var fixture = Create(P.All, dialogs: dialogs);
        await fixture.Channel(Bravo).EditCommand.ExecuteAsync(null);
        Assert.Equal(ChannelDialogMode.Edit, shownMode);
        Assert.Equal(new EditChannel(Bravo, "Bravo", "", 1, IsMuted: true), fixture.Sent[^1] with { RequestId = null });
        await fixture.Channel(Lobby).EditCommand.ExecuteAsync(null);
        Assert.Equal(ChannelDialogMode.EditDefault, shownMode);

        Assert.False(fixture.Channel(Bravo).IsMuted);
        fixture.Vm.Apply(new ChannelUpdated(new ChannelInfo(Bravo, "Bravo", "", 1, IsMuted: true)));
        Assert.True(fixture.Channel(Bravo).IsMuted);
    }

    /// <summary>Package 36: up and down send the complete order, the ends are disabled.</summary>
    [Fact]
    public async Task MoveUpDown_SendFullOrder_DisabledAtEdges()
    {
        var fixture = Create(P.ChannelEdit);
        Assert.Equal((false, true), (fixture.Channel(Lobby).CanMoveUp, fixture.Channel(Lobby).CanMoveDown));
        Assert.Equal((true, false), (fixture.Channel(Bravo).CanMoveUp, fixture.Channel(Bravo).CanMoveDown));

        await fixture.Channel(Bravo).MoveUpCommand.ExecuteAsync(null);
        Assert.Equal(new[] { Lobby, Bravo, Alpha }, ((ReorderChannels)fixture.Sent[^1]).ChannelIds);
        fixture.Vm.Apply(new Error(fixture.Sent[^1].RequestId, ReasonCodes.PermissionDenied)); // Package 98: refused, the server's order is back
        await fixture.Channel(Lobby).MoveDownCommand.ExecuteAsync(null);
        Assert.Equal(new[] { Alpha, Lobby, Bravo }, ((ReorderChannels)fixture.Sent[^1]).ChannelIds);

        var guest = Create();
        Assert.False(guest.Channel(Alpha).CanMoveUp); // without "Channels bearbeiten"
    }

    [Fact]
    public async Task DropOnChannel_AboveOrBelow_SendsOrderWithSourceBeforeOrAfterTarget()
    {
        var fixture = Create(P.ChannelEdit);
        await fixture.Vm.Tree.MoveChannelAsync(fixture.Channel(Bravo), fixture.Channel(Lobby), after: false);
        Assert.Equal(new[] { Bravo, Lobby, Alpha }, ((ReorderChannels)fixture.Sent[^1]).ChannelIds);
        fixture.Vm.Apply(new Error(fixture.Sent[^1].RequestId, ReasonCodes.PermissionDenied)); // Package 98: refused, the server's order is back
        await fixture.Vm.Tree.MoveChannelAsync(fixture.Channel(Lobby), fixture.Channel(Bravo), after: true);
        Assert.Equal(new[] { Alpha, Bravo, Lobby }, ((ReorderChannels)fixture.Sent[^1]).ChannelIds);
        fixture.Vm.Apply(new Error(fixture.Sent[^1].RequestId, ReasonCodes.PermissionDenied));

        int before = fixture.Sent.Count;
        await fixture.Vm.Tree.MoveChannelAsync(fixture.Channel(Alpha), fixture.Channel(Lobby), after: true); // already there
        await fixture.Vm.Tree.MoveChannelAsync(fixture.Channel(Alpha), fixture.Channel(Alpha), after: false);
        Assert.Equal(before, fixture.Sent.Count);
    }

    /// <summary>Package 37: the group tooltip follows the server's group order, not the order of assignment.</summary>
    [Fact]
    public void GroupNames_AssignedInOtherOrder_FollowGroupOrder()
    {
        var fixture = Create(others: [User(2, "anna", Lobby, P.Speak, groups: [WellKnownGroups.Admin, WellKnownGroups.Guest])]);
        Assert.Equal("Gast, Admin", fixture.User(2).GroupNames);
        fixture.Vm.Apply(new GroupsChanged([new GroupInfo(WellKnownGroups.Admin, "Admin", P.All), new GroupInfo(WellKnownGroups.Guest, "Gast", P.Speak)]));
        Assert.Equal("Admin, Gast", fixture.User(2).GroupNames);
    }

    // ---- Package 47: sounds ----

    static List<OVS.Client.Audio.Sounds.SoundEvent> Sounds(ServerViewModel server)
    {
        var heard = new List<OVS.Client.Audio.Sounds.SoundEvent>();
        server.SoundRequested += heard.Add;
        return heard;
    }

    [Fact]
    public void SoundEvents_Messages_Raised()
    {
        var fixture = Create(others: [User(2, "anna", Lobby, P.Speak), User(3, "bob", Bravo, P.Speak)]);
        var heard = Sounds(fixture.Vm);
        fixture.Vm.Apply(new UserJoined(User(4, "carla", Lobby, P.Speak)));              // into my channel
        fixture.Vm.Apply(new UserJoined(User(5, "dora", Bravo, P.Speak)));               // elsewhere: nothing
        fixture.Vm.Apply(new UserUpdated(User(3, "bob", Lobby, P.Speak)));               // comes over to me
        fixture.Vm.Apply(new UserUpdated(User(2, "anna", Alpha, P.Speak)));              // leaves my channel
        fixture.Vm.Apply(new UserLeft(4));                                   // gone from my channel
        fixture.Vm.Apply(new UserLeft(5));                                   // gone elsewhere: nothing
        fixture.Vm.Apply(new UserUpdated(User(1, "ich", Lobby, P.Speak) with { ServerMuted = true }));
        fixture.Vm.Apply(new UserUpdated(User(1, "ich", Bravo, P.Speak)));               // moved by someone else
        fixture.Vm.Apply(new ChatMessage(ChatTarget.Private, 3, "bob", null, 1, "psst", DateTimeOffset.UtcNow));
        fixture.Vm.Apply(new ChatMessage(ChatTarget.Private, 1, "ich", null, 3, "ja", DateTimeOffset.UtcNow)); // own echo: nothing
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
        var fixture = Create(others: [User(2, "anna", Lobby, P.Speak)]);
        var heard = Sounds(fixture.Vm);
        fixture.Vm.Apply(new UserUpdated(User(1, "ich", Lobby, P.Speak, groups: [WellKnownGroups.Guest]) with { SelfMuted = true })); // no group change
        fixture.Vm.Apply(new UserUpdated(User(1, "ich", Lobby, P.All, [WellKnownGroups.Guest, WellKnownGroups.Admin]))); // given
        fixture.Vm.Apply(new UserUpdated(User(1, "ich", Lobby, P.All, [WellKnownGroups.Guest, WellKnownGroups.Admin]))); // repeated: no change
        fixture.Vm.Apply(new UserUpdated(User(1, "ich", Lobby, P.Speak, [WellKnownGroups.Guest]))); // taken away
        fixture.Vm.Apply(new UserUpdated(User(2, "anna", Lobby, P.None, [WellKnownGroups.Admin]))); // somebody else (Package 92: without rights)
        Assert.Equal([SoundEvent.GroupChanged, SoundEvent.GroupChanged], heard);
    }

    /// <summary>Package 73: changing the own groups in the admin page gives only the tone for the one acting.</summary>
    [Fact]
    public async Task OwnGroups_ChangedByMe_OnlyOneSound()
    {
        var fixture = Create(P.All | P.GroupsAssign, [WellKnownGroups.Guest, WellKnownGroups.Admin]);
        var admin = new AdminViewModel(fixture.Vm);
        admin.UsersAndBans.Apply(new UserList("r", [new KnownUserInfo("fp1", "ich", [WellKnownGroups.Guest, WellKnownGroups.Admin])]));
        var heard = Sounds(fixture.Vm);
        var guest = Assert.Single(admin.UsersAndBans.Users).Toggles.Single(groupAssignment => groupAssignment.GroupId == WellKnownGroups.Guest);
        guest.IsChecked = false;
        await guest.ToggleCommand.ExecuteAsync(null);
        fixture.Vm.Apply(new UserUpdated(User(1, "ich", Lobby, P.All, [WellKnownGroups.Admin]))); // the server tells me first
        admin.UsersAndBans.Apply(new UserList("r", [new KnownUserInfo("fp1", "ich", [WellKnownGroups.Admin])]));
        Assert.Equal([SoundEvent.GroupChangedByMe], heard);

        fixture.Vm.Apply(new UserUpdated(User(1, "ich", Lobby, P.All, [WellKnownGroups.Guest, WellKnownGroups.Admin]))); // someone else gives it back
        Assert.Equal([SoundEvent.GroupChangedByMe, SoundEvent.GroupChanged], heard);
    }

    /// <summary>Package 57: someone talking in over a link is announced by a short tone, once per transmission.</summary>
    [Fact]
    public void LinkVoice_TalkingInOverALink_SoundOncePerTransmission()
    {
        var fixture = Create(others: [User(2, "anna", Bravo, P.Speak)]);
        var heard = Sounds(fixture.Vm);
        void Talk(int milliseconds)
        {
            for (int elapsed = 0; elapsed < milliseconds; elapsed += 20)
            {
                fixture.Vm.Tree.OnSpeakers([new ActiveSpeaker(2, ViaLink: true)]);
                fixture.Time.Advance(TimeSpan.FromMilliseconds(20));
            }
        }
        Talk(1000);
        Assert.Equal([OVS.Client.Audio.Sounds.SoundEvent.LinkVoice], heard);
        fixture.Time.Advance(TimeSpan.FromMilliseconds(200)); // a short breath: still the same transmission
        Talk(300);
        Assert.Single(heard);
        fixture.Time.Advance(TimeSpan.FromMilliseconds(400)); // a real pause: a new transmission
        Talk(300);
        Assert.Equal(2, heard.Count);
    }

    /// <summary>Package 60: starting to talk over a link oneself gives an own tone, once per transmission.</summary>
    [Fact]
    public void OwnLinkVoice_StartTalkingOverALink_SoundOncePerTransmission()
    {
        var fixture = Create();
        fixture.Vm.Apply(new ChannelsLinked(Lobby, Bravo));
        var heard = Sounds(fixture.Vm);
        const byte Linked = OVS.Shared.Voice.VoiceHeader.TargetLinked;
        fixture.Vm.Tree.SetSelfTransmitting(Linked);
        fixture.Vm.Tree.SetSelfTransmitting(Linked); // still the same transmission
        Assert.Equal([OVS.Client.Audio.Sounds.SoundEvent.OwnLinkVoice], heard);

        fixture.Vm.Tree.SetSelfTransmitting(null);
        fixture.Time.Advance(TimeSpan.FromMilliseconds(100)); // let go and pressed again at once
        fixture.Vm.Tree.SetSelfTransmitting(Linked);
        Assert.Single(heard);

        fixture.Vm.Tree.SetSelfTransmitting(null);
        fixture.Time.Advance(TimeSpan.FromMilliseconds(400));
        fixture.Vm.Tree.SetSelfTransmitting(Linked);
        Assert.Equal(2, heard.Count);
    }

    [Fact]
    public void OwnLinkVoice_NoLinksOrChannelOnly_NoSound()
    {
        var fixture = Create();
        var heard = Sounds(fixture.Vm);
        fixture.Vm.Tree.SetSelfTransmitting(OVS.Shared.Voice.VoiceHeader.TargetLinked); // no link from my channel: nobody else hears it
        fixture.Vm.Tree.SetSelfTransmitting(null);
        fixture.Vm.Apply(new ChannelsLinked(Lobby, Bravo));
        fixture.Time.Advance(TimeSpan.FromSeconds(1));
        fixture.Vm.Tree.SetSelfTransmitting(OVS.Shared.Voice.VoiceHeader.TargetChannel); // only the own channel, e.g. without the right
        Assert.Empty(heard);
    }

    [Fact]
    public void LinkVoice_OwnChannelOrSelf_NoSound()
    {
        var fixture = Create(others: [User(2, "anna", Lobby, P.Speak)]);
        var heard = Sounds(fixture.Vm);
        fixture.Vm.Tree.OnSpeakers([new ActiveSpeaker(2, ViaLink: false)]); // in my channel
        fixture.Vm.Tree.OnSpeakers([new ActiveSpeaker(1, ViaLink: true)]); // myself
        fixture.Vm.Tree.OnSpeakers([new ActiveSpeaker(OVS.Client.Audio.AudioEngine.SelfTestSpeaker, ViaLink: true)]); // the self test
        Assert.Empty(heard);
    }

    /// <summary>Package 56: "Allgemein" and the channel chat have their own sound; nobody hears their own messages.</summary>
    [Fact]
    public void ChatMessages_SoundPerTarget_NotForOwn()
    {
        var fixture = Create(others: [User(2, "anna", Lobby, P.Speak)]);
        var heard = Sounds(fixture.Vm);
        fixture.Vm.Apply(new ChatMessage(ChatTarget.Server, 2, "anna", null, null, "Hallo alle", DateTimeOffset.UtcNow));
        fixture.Vm.Apply(new ChatMessage(ChatTarget.Channel, 2, "anna", Lobby, null, "Hallo Lobby", DateTimeOffset.UtcNow));
        fixture.Vm.Apply(new ChatMessage(ChatTarget.Private, 2, "anna", null, 1, "psst", DateTimeOffset.UtcNow));
        fixture.Vm.Apply(new ChatMessage(ChatTarget.Server, 1, "ich", null, null, "selbst", DateTimeOffset.UtcNow));
        fixture.Vm.Apply(new ChatMessage(ChatTarget.Channel, 1, "ich", Lobby, null, "selbst", DateTimeOffset.UtcNow));
        Assert.Equal([OVS.Client.Audio.Sounds.SoundEvent.ServerMessage, OVS.Client.Audio.Sounds.SoundEvent.ChannelMessage, OVS.Client.Audio.Sounds.SoundEvent.PrivateMessage], heard);
    }

    [Fact]
    public async Task SoundEvents_OwnChanges_NotDoubled()
    {
        var fixture = Create();
        var heard = Sounds(fixture.Vm);
        await fixture.Vm.SelfState.ToggleMuteCommand.ExecuteAsync(null);
        await fixture.Vm.SelfState.ToggleMuteCommand.ExecuteAsync(null);
        await fixture.Vm.SelfState.ToggleDeafenCommand.ExecuteAsync(null);
        await fixture.Vm.SelfState.ToggleMuteCommand.ExecuteAsync(null); // unmuting while deafened turns the sound back on
        await fixture.Vm.JoinAsync(Bravo);
        fixture.Vm.Apply(new UserUpdated(User(1, "ich", Bravo, P.Speak) with { SelfMuted = false }));
        fixture.Vm.Apply(new UserUpdated(User(1, "ich", Bravo, P.Speak) with { SelfMuted = true })); // the server's echo of the own mute: no second tone
        Assert.Equal(
        [
            OVS.Client.Audio.Sounds.SoundEvent.MicOff, OVS.Client.Audio.Sounds.SoundEvent.MicOn, OVS.Client.Audio.Sounds.SoundEvent.SoundOff,
            OVS.Client.Audio.Sounds.SoundEvent.SoundOn, OVS.Client.Audio.Sounds.SoundEvent.ChannelEntered,
        ], heard);
    }

    [Fact]
    public void SlotText_LimitedOrUnlimited_Formatted()
    {
        var fixture = Create(others: [User(2, "anna", Bravo, P.Speak)]);
        Assert.Equal("1", fixture.Channel(Bravo).SlotText);
        fixture.Vm.Apply(new ChannelUpdated(new ChannelInfo(Bravo, "Bravo", "", 1, MaxUsers: 5)));
        Assert.Equal("1/5", fixture.Channel(Bravo).SlotText);
        Assert.Equal(5, fixture.Channel(Bravo).MaxUsers);
    }

    [Fact]
    public void SlotText_UserJoins_ChangeRaised()
    {
        var fixture = Create(others: [User(2, "anna", Bravo, P.Speak)]);
        var changed = new List<string?>();
        fixture.Channel(Bravo).PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        fixture.Vm.Apply(new UserJoined(User(3, "bert", Bravo, P.Speak)));
        Assert.Equal("2", fixture.Channel(Bravo).SlotText);
        Assert.Contains(nameof(ChannelViewModel.SlotText), changed); // the counter's text follows
    }

    [Fact]
    public void MaxUsers_LimitChanges_ChangeRaised()
    {
        var fixture = Create(others: [User(2, "anna", Bravo, P.Speak)]);
        var changed = new List<string?>();
        fixture.Channel(Bravo).PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        fixture.Vm.Apply(new ChannelUpdated(new ChannelInfo(Bravo, "Bravo", "", 1, MaxUsers: 5)));
        Assert.Equal(5, fixture.Channel(Bravo).MaxUsers);
        Assert.Contains(nameof(ChannelViewModel.MaxUsers), changed);
    }

    [Fact]
    public void RedeemToken_Admin_Hidden()
    {
        Assert.True(Create().Vm.CanRedeemToken);
        Assert.False(Create(P.All, [WellKnownGroups.Admin]).Vm.CanRedeemToken);
    }

    [Fact]
    public async Task Ban_Confirmed_SendsDurationAndIpFlag()
    {
        var dialogs = new ServerDialogs { Ban = (_, _, _) => Task.FromResult<BanChoice?>(new BanChoice("spam", 60, true)) };
        var fixture = Create(Moderator, dialogs: dialogs, others: User(2, "gast", Lobby, P.Speak));
        await fixture.User(2).BanCommand.ExecuteAsync(null);
        Assert.Equal(new BanSession(2, "spam", 60, true), fixture.Sent[^1] with { RequestId = null });
    }

    [Fact]
    public async Task ChannelDialog_Cancelled_SendsNothing()
    {
        var fixture = Create(P.All, others: User(2, "gast", Lobby, P.Speak));
        await fixture.User(2).KickCommand.ExecuteAsync(null);
        await fixture.Channel(Alpha).DeleteCommand.ExecuteAsync(null);
        await fixture.Vm.NewChannelCommand.ExecuteAsync(null);
        Assert.Empty(fixture.Sent);
    }

    // ---- Package 98 (A113): waiting states ----

    /// <summary>Package 98 (AC1): the target row spins until the own channel changes or an error comes; a second join meanwhile is ignored.</summary>
    [Fact]
    public async Task JoinChannel_RowBusyUntilMoved_SecondJoinIgnored()
    {
        var fixture = Create();
        var notices = new List<string>();
        fixture.Vm.Notice += notices.Add;
        var bravo = fixture.Channel(Bravo);
        await bravo.JoinCommand.ExecuteAsync(null);
        fixture.Time.Advance(PendingActionViewModel.ShowAfter - TimeSpan.FromMilliseconds(1));
        Assert.False(bravo.IsBusy); // a fast answer never flickers
        fixture.Time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(bravo.IsBusy);
        await fixture.Channel(Alpha).JoinCommand.ExecuteAsync(null);
        Assert.Single(fixture.Sent.OfType<JoinChannel>());
        fixture.Vm.Apply(new UserUpdated(User(1, "ich", Bravo, P.Speak)));
        Assert.False(bravo.IsBusy);
        Assert.True(bravo.IsCurrent);

        // refused: the spinner goes and the reason shows; joining works again
        var alpha = fixture.Channel(Alpha);
        await alpha.JoinCommand.ExecuteAsync(null);
        fixture.Time.Advance(PendingActionViewModel.ShowAfter);
        Assert.True(alpha.IsBusy);
        fixture.Vm.Apply(new Error(fixture.Sent[^1].RequestId, ReasonCodes.ChannelFull));
        Assert.False(alpha.IsBusy);
        Assert.Equal(ErrorTexts.For(ReasonCodes.ChannelFull), notices[^1]);

        // no answer at all: a visible error after 10 s, then it can be tried again
        await alpha.JoinCommand.ExecuteAsync(null);
        fixture.Time.Advance(TimeSpan.FromSeconds(10));
        Assert.False(alpha.IsBusy);
        Assert.Equal(OVS.Client.Localization.Strings.Pending_NoAnswer, notices[^1]);
        await alpha.JoinCommand.ExecuteAsync(null);
        Assert.Equal(4, fixture.Sent.OfType<JoinChannel>().Count());
    }

    /// <summary>Package 98 (AC4): a dropped channel stays in its new place until the server confirms; refused, it goes back.</summary>
    [Fact]
    public async Task Reorder_Refused_Reverted()
    {
        var fixture = Create(P.ChannelEdit);
        Guid[] Order() => fixture.Vm.Tree.Channels.Select(channel => channel.Id).ToArray();
        Assert.Equal([Lobby, Alpha, Bravo], Order());

        await fixture.Vm.Tree.MoveChannelAsync(fixture.Channel(Bravo), fixture.Channel(Lobby), after: false);
        var refused = Assert.IsType<ReorderChannels>(fixture.Sent[^1]);
        Assert.Equal([Bravo, Lobby, Alpha], Order()); // at once, no jump back
        fixture.Vm.Apply(new UserJoined(User(2, "anna", Lobby, P.Speak))); // something else changes meanwhile: the new order stays
        Assert.Equal([Bravo, Lobby, Alpha], Order());
        fixture.Time.Advance(PendingActionViewModel.ShowAfter);
        Assert.True(fixture.Channel(Bravo).IsBusy);
        fixture.Vm.Apply(new Error(refused.RequestId, ReasonCodes.PermissionDenied));
        Assert.Equal([Lobby, Alpha, Bravo], Order());
        Assert.False(fixture.Channel(Bravo).IsBusy);

        // accepted: the server's updates confirm the order
        await fixture.Vm.Tree.MoveChannelAsync(fixture.Channel(Bravo), fixture.Channel(Lobby), after: false);
        fixture.Time.Advance(PendingActionViewModel.ShowAfter);
        fixture.Vm.Apply(new ChannelUpdated(new ChannelInfo(Bravo, "Bravo", "", 0)));
        fixture.Vm.Apply(new ChannelUpdated(new ChannelInfo(Lobby, "Lobby", "", 1)));
        fixture.Vm.Apply(new ChannelUpdated(new ChannelInfo(Alpha, "alpha", "", 2)));
        Assert.Equal([Bravo, Lobby, Alpha], Order());
        Assert.False(fixture.Channel(Bravo).IsBusy);
        fixture.Time.Advance(TimeSpan.FromSeconds(10)); // no late timeout puts anything back
        Assert.Equal([Bravo, Lobby, Alpha], Order());

        // no answer: back after 10 s
        await fixture.Vm.Tree.MoveChannelAsync(fixture.Channel(Alpha), fixture.Channel(Bravo), after: false);
        Assert.Equal([Alpha, Bravo, Lobby], Order());
        fixture.Time.Advance(TimeSpan.FromSeconds(10));
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
        var fixture = Create(Moderator | P.ChannelCreate | P.ChannelDelete, dialogs: dialogs, others: User(2, "gast", Lobby, P.Speak));
        await fixture.Vm.NewChannelCommand.ExecuteAsync(null);
        Assert.Single(fixture.Sent.OfType<CreateChannel>()); // sent once, by the dialog's submit
        Assert.True(waited!.IsRunning);
        var created = Guid.NewGuid();
        fixture.Vm.Apply(new ChannelAdded(new ChannelInfo(Guid.NewGuid(), "Neu", "", 4))); // Package 110: someone else's "Neu"
        Assert.True(waited.IsRunning);
        fixture.Vm.Apply(new ChannelAdded(new ChannelInfo(created, "Neu", "", 3), fixture.Sent.OfType<CreateChannel>().Single().RequestId));
        Assert.False(waited.IsRunning);
        Assert.Null(await waited.Completion);

        // the mute toggle spins on the user's row until the server's update
        var gast = fixture.User(2);
        await gast.ToggleServerMuteCommand.ExecuteAsync(null);
        fixture.Time.Advance(PendingActionViewModel.ShowAfter);
        Assert.True(gast.IsBusy);
        fixture.Vm.Apply(new UserUpdated(User(2, "gast", Lobby, P.Speak) with { ServerMuted = true }));
        Assert.False(gast.IsBusy);

        // deleting a channel spins on its row until it is gone
        var channel = fixture.Channel(created);
        await channel.DeleteCommand.ExecuteAsync(null);
        fixture.Time.Advance(PendingActionViewModel.ShowAfter);
        Assert.True(channel.IsBusy);
        fixture.Vm.Apply(new ChannelRemoved(created));
        Assert.False(channel.IsBusy);
    }

    /// <summary>Package 102: a user or channel folding away is no target, no count and no neighbour any more.</summary>
    [Fact]
    public void LeavingUser_NotCountedNotTarget_LeavingChannelSkipped()
    {
        var clock = new ManualTimeProvider();
        var sent = new List<Request>();
        var server = FakeServers.Admin(sent, time: clock);
        server.Post = action => action();
        server.Leave.Delay = TimeSpan.FromMinutes(1);
        var raid = server.Tree.Channels.Single(channel => channel.Name == "Raid");
        server.Apply(new UserLeft(2));
        Assert.Contains(raid.Users, user => user.SessionId == 2); // still shown, folding away
        Assert.True(CollectionSync.IsLeaving(raid.Users, raid.Users.Single()));
        Assert.Equal("0", raid.SlotText);

        var archiv = Guid.NewGuid();
        server.Apply(new ChannelAdded(new ChannelInfo(archiv, "Archiv", "", 5)));
        server.Apply(new ChannelRemoved(FakeServers.Raid));
        Assert.Contains(server.Tree.Channels, channel => channel.Name == "Raid");
        Assert.Equal(["Lobby", "Archiv"], server.Tree.Channels.Live().Select(channel => channel.Name));
        var lobby = server.Tree.Channels.Single(channel => channel.Name == "Lobby");
        Assert.DoesNotContain(server.Tree.LinkCandidates(lobby), channel => channel.Name == "Raid");

        // Lobby's neighbour below is Archiv, not the folding Raid
        lobby.MoveDownCommand.Execute(null);
        Assert.Equal([archiv, FakeServers.Lobby], sent.OfType<ReorderChannels>().Single().ChannelIds);
    }

    /// <summary>Package 102: someone who comes back while folding away is the same entry again.</summary>
    [Fact]
    public void UserEntry_BackWhileLeaving_SameEntry()
    {
        var clock = new ManualTimeProvider();
        var server = FakeServers.Admin(time: clock);
        server.Post = action => action();
        server.Leave.Delay = TimeSpan.FromMinutes(1);
        var raid = server.Tree.Channels.Single(channel => channel.Name == "Raid");
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
        server.Post = action => action();
        server.Leave.Delay = TimeSpan.FromMinutes(1);
        var lobby = server.Tree.Channels.Single(channel => channel.Name == "Lobby");
        var raid = server.Tree.Channels.Single(channel => channel.Name == "Raid");
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
        var fixture = Create(P.All, others: User(2, "anna", Lobby, P.Speak) with { CanBeModeratedByMe = true });
        var id = Guid.NewGuid();
        fixture.Vm.Apply(new ChannelAdded(new ChannelInfo(id, "", "", 5, Kind: ChannelKind.Separator)));
        var separator = fixture.Channel(id);
        Assert.True(separator.IsSeparator);
        Assert.False(separator.IsVoice);
        Assert.Equal((false, false, false), (separator.CanJoin, separator.CanEditDetails, separator.CanLink));
        Assert.Equal((true, true), (separator.CanEdit, separator.CanDelete)); // moves and goes like a channel
        Assert.Null(separator.Tooltip);
        Assert.Equal("0", separator.SlotText);

        await separator.JoinCommand.ExecuteAsync(null); // also the double click
        Assert.Empty(fixture.Sent.OfType<JoinChannel>());
        Assert.DoesNotContain(separator, fixture.Vm.Tree.LinkCandidates(fixture.Channel(Lobby)));
        Assert.NotEmpty(fixture.User(2).MoveTargets);
        Assert.DoesNotContain(separator, fixture.User(2).MoveTargets.Select(moveTarget => moveTarget.Channel)); // Package 120: the submenu
    }

    /// <summary>Package 112: without description and links there is no tooltip at all (an empty text showed an empty box).</summary>
    [Fact]
    public void Tooltip_NoDescriptionNoLinks_IsNull()
    {
        var fixture = Create();
        Assert.Null(fixture.Channel(Bravo).Tooltip);
        fixture.Vm.Apply(new ChannelUpdated(new ChannelInfo(Bravo, "Bravo", "   ", 1)));
        Assert.Null(fixture.Channel(Bravo).Tooltip); // only spaces count as none
        // the same for a user row: in no known group, no tooltip
        var otherFixture = Create(others: [User(2, "anna", Lobby, P.Speak, groups: [])]);
        Assert.Null(otherFixture.User(2).GroupTip);
        Assert.Equal("Gast", otherFixture.User(1).GroupTip);
    }

    [Fact]
    public void Tooltip_DescriptionOrLinks_Unchanged()
    {
        var fixture = Create();
        fixture.Vm.Apply(new ChannelUpdated(new ChannelInfo(Bravo, "Bravo", "Nur Raid", 1)));
        Assert.Equal("Nur Raid", fixture.Channel(Bravo).Tooltip);
        fixture.Vm.Apply(new ChannelsLinked(Lobby, Alpha));
        Assert.Equal(string.Format(OVS.Client.Localization.Strings.Tooltip_LinkedWith, "Lobby"), fixture.Channel(Alpha).Tooltip);
        fixture.Vm.Apply(new ChannelsLinked(Bravo, Alpha));
        Assert.Equal("Nur Raid" + (char)10 + string.Format(OVS.Client.Localization.Strings.Tooltip_LinkedWith, "alpha"), fixture.Channel(Bravo).Tooltip);
    }

    // ---- Package 119 (A128): groups from the user's context menu ----

    static readonly Guid Raid = Guid.NewGuid();

    /// <summary>Guest and Raid assignable by me, Admin not (stronger than me).</summary>
    static readonly GroupInfo[] AssignGroups =
        [new(WellKnownGroups.Guest, "Gast", P.Speak, true), new(WellKnownGroups.Admin, "Admin", P.All), new(Raid, "Raid", P.Speak, true)];

    static UserInfo Weaker(IReadOnlyList<Guid>? groups = null) => User(2, "anna", Lobby, P.Speak, groups: groups) with { CanBeModeratedByMe = true };

    static List<GroupTick> Ticks(UserViewModel user) => user.Groups.Select(groupAssignment => new GroupTick(groupAssignment.GroupId, groupAssignment.Name, groupAssignment.IsChecked, groupAssignment.IsEnabled)).ToList();

    /// <summary>Package 119 (AC1, AC2): the submenu only for weaker users and only with GroupsAssign; a group I may not give is greyed out.</summary>
    [Fact]
    public void UserGroupsMenu_WeakerUserAndAssignRight_OnlyThenShown()
    {
        var fixture = Create(Moderator | P.GroupsAssign, groups: AssignGroups, others: [Weaker(), User(3, "bert", Lobby, P.Speak)]);
        Assert.Empty(fixture.User(1).Groups); // myself
        Assert.Empty(fixture.User(3).Groups); // stronger or equal: the server does not let me act on him
        Assert.Equal([new GroupTick(WellKnownGroups.Guest, "Gast", true, true), new GroupTick(WellKnownGroups.Admin, "Admin", false, false), new GroupTick(Raid, "Raid", false, true)],
            Ticks(fixture.User(2)));

        var without = Create(Moderator, groups: AssignGroups, others: [Weaker()]);
        Assert.Empty(without.User(2).Groups); // no GroupsAssign
    }

    /// <summary>Package 119 (AC3, AC4): a click sends at once, the tick and tooltip follow the server; a refusal puts the tick back.</summary>
    [Fact]
    public async Task UserGroupToggle_SendsAssign_ConfirmedByUserUpdated()
    {
        var fixture = Create(Moderator | P.GroupsAssign, groups: AssignGroups, others: [Weaker()]);
        var notices = new List<string>();
        fixture.Vm.Notice += notices.Add;
        var anna = fixture.User(2);

        var raid = anna.Groups.Single(groupAssignment => groupAssignment.GroupId == Raid);
        raid.IsChecked = true; // the menu item ticks itself on the click, then runs the command
        await raid.ToggleCommand.ExecuteAsync(null);
        Assert.Equal(new AssignGroup("fp2", Raid), fixture.Sent[^1] with { RequestId = null });
        fixture.Time.Advance(PendingActionViewModel.ShowAfter);
        Assert.True(anna.IsBusy);
        fixture.Vm.Apply(new UserUpdated(Weaker([WellKnownGroups.Guest]) with { SelfMuted = true })); // another change meanwhile
        Assert.True(anna.Groups.Single(groupAssignment => groupAssignment.GroupId == Raid).IsChecked); // the tick stays until the server shows the group
        Assert.True(anna.IsBusy);
        fixture.Vm.Apply(new UserUpdated(Weaker([WellKnownGroups.Guest, Raid])));
        Assert.False(anna.IsBusy);
        Assert.True(anna.Groups.Single(groupAssignment => groupAssignment.GroupId == Raid).IsChecked);
        Assert.Equal("Gast, Raid", anna.GroupTip);

        // taken away again, but refused: the error shows and the tick goes back
        raid = anna.Groups.Single(groupAssignment => groupAssignment.GroupId == Raid);
        raid.IsChecked = false;
        await raid.ToggleCommand.ExecuteAsync(null);
        Assert.Equal(new UnassignGroup("fp2", Raid), fixture.Sent[^1] with { RequestId = null });
        fixture.Time.Advance(PendingActionViewModel.ShowAfter);
        Assert.True(anna.IsBusy);
        fixture.Vm.Apply(new Error(fixture.Sent[^1].RequestId, ReasonCodes.PermissionDenied));
        Assert.False(anna.IsBusy);
        Assert.Equal(ErrorTexts.For(ReasonCodes.PermissionDenied), notices[^1]);
        Assert.True(anna.Groups.Single(groupAssignment => groupAssignment.GroupId == Raid).IsChecked);

        // a change made elsewhere shows the next time the menu opens
        fixture.Vm.Apply(new UserUpdated(Weaker([Raid])));
        Assert.Equal([false, false, true], anna.Groups.Select(groupAssignment => groupAssignment.IsChecked));
    }

    /// <summary>Package 119 (review): on, then off before any answer; the first answer neither shows the tick nor ends the spinner.</summary>
    [Fact]
    public async Task UserGroupToggle_OnThenOff_LastClickWins()
    {
        var fixture = Create(Moderator | P.GroupsAssign, groups: AssignGroups, others: [Weaker()]);
        var anna = fixture.User(2);
        GroupAssignmentViewModel Raid_() => anna.Groups.Single(groupAssignment => groupAssignment.GroupId == Raid);
        var raid = Raid_();
        raid.IsChecked = true;
        await raid.ToggleCommand.ExecuteAsync(null);
        raid = Raid_();
        raid.IsChecked = false;
        await raid.ToggleCommand.ExecuteAsync(null);
        Assert.IsType<UnassignGroup>(fixture.Sent[^1]);
        fixture.Time.Advance(PendingActionViewModel.ShowAfter);
        Assert.True(anna.IsBusy);

        fixture.Vm.Apply(new UserUpdated(Weaker([WellKnownGroups.Guest, Raid]))); // confirms the first click only
        Assert.False(Raid_().IsChecked);
        Assert.True(anna.IsBusy);
        fixture.Vm.Apply(new UserUpdated(Weaker([WellKnownGroups.Guest])));
        Assert.False(Raid_().IsChecked);
        Assert.False(anna.IsBusy);
    }

    /// <summary>
    /// Package 119 (AC2): the last admin keeps the Admin group. An Admin-group member has every right, so the server never
    /// judges one weaker than me and the menu has no groups for him, the same rule that keeps the administration's toggles off.
    /// </summary>
    [Fact]
    public void AdminGroup_LastAdmin_CannotBeUnticked()
    {
        var fixture = Create(Moderator | P.GroupsAssign, groups: AssignGroups, others: [User(2, "chef", Lobby, P.All, [WellKnownGroups.Admin])]);
        Assert.Empty(fixture.User(2).Groups);
    }

    // ---- Package 120 (A129): moving users from the menu and by drag and drop ----

    /// <summary>Package 120 (AC1): the other channels in tree order, no separator; a click moves at once, without the dialog.</summary>
    [Fact]
    public void MoveTargets_ChannelTree_OtherChannelsWithoutSeparators()
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
        var fixture = Create(Moderator, dialogs: dialogs, others: [Weaker(), User(3, "bert", Lobby, P.Speak)]);
        fixture.Vm.Apply(new ChannelAdded(new ChannelInfo(Guid.NewGuid(), "", "", 5, Kind: ChannelKind.Separator)));
        var anna = fixture.User(2);
        Assert.Equal(["alpha", "Bravo"], anna.MoveTargets.Select(moveTarget => moveTarget.Channel.Name));
        Assert.True(anna.HasMoveTargets);
        Assert.Empty(fixture.User(1).MoveTargets); // myself: that is joining
        Assert.Empty(fixture.User(3).MoveTargets); // not weaker than me
        Assert.Empty(Create(P.Speak, others: [Weaker()]).User(2).MoveTargets); // without UserMove

        anna.MoveTargets.Single(moveTarget => moveTarget.Channel.Id == Bravo).Command.Execute(null);
        Assert.Equal(new MoveUser(2, Bravo), fixture.Sent[^1] with { RequestId = null });
        Assert.False(picked);
        fixture.Time.Advance(PendingActionViewModel.ShowAfter);
        Assert.True(anna.IsBusy); // Package 98: the row spins until the server shows the move
        fixture.Vm.Apply(new UserUpdated(Weaker() with { ChannelId = Bravo }));
        Assert.False(anna.IsBusy);
        Assert.Equal(["Lobby", "alpha"], anna.MoveTargets.Select(moveTarget => moveTarget.Channel.Name)); // follows the user
    }

    /// <summary>Package 120 (AC5): a refused move shows the server's error; the user stays where they were.</summary>
    [Fact]
    public async Task Move_Refused_ShowsErrorAndUserStays()
    {
        var fixture = Create(Moderator, others: [Weaker()]);
        var notices = new List<string>();
        fixture.Vm.Notice += notices.Add;
        var anna = fixture.User(2);
        Assert.True(anna.CanDrag);
        var done = fixture.Vm.DropUserAsync(anna, fixture.Channel(Bravo));
        Assert.Equal(new MoveUser(2, Bravo), fixture.Sent[^1] with { RequestId = null });
        Assert.False(done.IsCompleted); // waits for the server
        fixture.Vm.Apply(new Error(fixture.Sent[^1].RequestId, ReasonCodes.ChannelFull));
        await done;
        Assert.Equal(ErrorTexts.For(ReasonCodes.ChannelFull), notices[^1]);
        Assert.Equal(Lobby, anna.ChannelId);
        Assert.Contains(anna, fixture.Channel(Lobby).Users);
        Assert.False(anna.IsBusy);
    }

    /// <summary>Package 120 (review): a user who left the server meanwhile is not moved by a drop of their row.</summary>
    [Fact]
    public void DropUser_LeftMeanwhile_SendsNothing()
    {
        var fixture = Create(Moderator, others: [Weaker()]);
        var anna = fixture.User(2);
        fixture.Vm.Apply(new UserLeft(2));
        Assert.True(fixture.Vm.DropUserAsync(anna, fixture.Channel(Bravo)).IsCompleted); // nothing to wait for
        Assert.Empty(fixture.Sent);
    }

    /// <summary>Package 120 (AC3, AC4): the own row joins (with the password prompt); the own channel and a separator send nothing.</summary>
    [Fact]
    public async Task DropUser_OwnRowJoins_InvalidTargetsSendNothing()
    {
        var asked = new List<string>();
        var fixture = Create(Moderator, dialogs: new ServerDialogs { AskChannelPassword = name => { asked.Add(name); return Task.FromResult<string?>("geheim"); } },
            others: [Weaker(), User(3, "bert", Lobby, P.Speak)]);
        var separator = Guid.NewGuid();
        fixture.Vm.Apply(new ChannelAdded(new ChannelInfo(separator, "", "", 5, Kind: ChannelKind.Separator)));
        fixture.Vm.Apply(new ChannelUpdated(new ChannelInfo(Bravo, "Bravo", "", 1, HasPassword: true)));
        var (me, anna, bert) = (fixture.User(1), fixture.User(2), fixture.User(3));
        Assert.Equal((true, false), (me.CanDrag, bert.CanDrag));
        Assert.False(ServerViewModel.IsDropTarget(anna, fixture.Channel(Lobby))); // her own channel: the server would do nothing
        Assert.False(ServerViewModel.IsDropTarget(anna, fixture.Channel(separator)));
        Assert.False(ServerViewModel.IsDropTarget(bert, fixture.Channel(Bravo)));
        Assert.True(ServerViewModel.IsDropTarget(anna, fixture.Channel(Bravo)));
        await fixture.Vm.DropUserAsync(anna, fixture.Channel(Lobby));
        await fixture.Vm.DropUserAsync(bert, fixture.Channel(Bravo));
        await fixture.Vm.DropUserAsync(me, fixture.Channel(separator));
        Assert.Empty(fixture.Sent);

        var joined = fixture.Vm.DropUserAsync(me, fixture.Channel(Bravo));
        Assert.Equal(["Bravo"], asked);
        Assert.Equal(new JoinChannel(Bravo, "geheim"), fixture.Sent[^1] with { RequestId = null });
        fixture.Vm.Apply(new UserUpdated(User(1, "ich", Bravo, Moderator)));
        await joined;
        Assert.Equal(Bravo, fixture.Vm.CurrentChannel!.Id);
    }

    sealed record Fixture(ServerViewModel Vm, List<Request> Sent, ManualTimeProvider Time)
    {
        public ChannelViewModel Channel(Guid id) => Vm.Tree.Channels.Single(channel => channel.Id == id);
        public UserViewModel User(uint id) => Vm.Tree.Channels.SelectMany(channel => channel.Users).Single(user => user.SessionId == id);
    }

    sealed record GroupTick(Guid GroupId, string Name, bool IsChecked, bool IsEnabled);
}
