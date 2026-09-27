using OVS.Client;
using OVS.Client.Audio;
using OVS.Client.Net;
using OVS.Client.ViewModels;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Shared.Voice;
using OVS.Tests.TestSupport;
using P = OVS.Shared.Permissions.Permission;

namespace OVS.Tests.Client;

public class ServerViewModelTests
{
    static readonly Guid Lobby = Guid.NewGuid(), Alpha = Guid.NewGuid(), Bravo = Guid.NewGuid();
    const P Moderator = P.Speak | P.SpeakLinked | P.ChannelLink | P.UserMove | P.UserMute | P.UserKick | P.UserBan;

    static UserInfo U(uint id, string nick, Guid channel, P perms = P.Speak, IReadOnlyList<Guid>? groups = null) =>
        new(id, "fp" + id, nick, channel, false, false, false, perms, groups ?? [WellKnownGroups.Guest]);

    sealed record Fixture(ServerViewModel Vm, List<Request> Sent, ManualTimeProvider Time)
    {
        public ChannelViewModel Channel(Guid id) => Vm.Channels.Single(c => c.Id == id);
        public UserViewModel User(uint id) => Vm.Channels.SelectMany(c => c.Users).Single(u => u.SessionId == id);
    }

    static Fixture Create(P selfPerms = P.Speak, IReadOnlyList<Guid>? selfGroups = null, Dialogs? dialogs = null, params UserInfo[] others)
    {
        var snapshot = new ServerSnapshot(new ServerSettingsInfo("Server", "Hallo", false), Lobby,
            [new ChannelInfo(Lobby, "Lobby", "", 0), new ChannelInfo(Bravo, "Bravo", "", 1), new ChannelInfo(Alpha, "alpha", "", 1)],
            [],
            [new GroupInfo(WellKnownGroups.Guest, "Gast", P.Speak), new GroupInfo(WellKnownGroups.Admin, "Admin", P.All)],
            [U(1, "ich", Lobby, selfPerms, selfGroups), .. others]);
        var sent = new List<Request>();
        var time = new ManualTimeProvider();
        var vm = new ServerViewModel(new StateMirror(new Welcome(1, "", snapshot)), r =>
        {
            sent.Add(r);
            return Task.CompletedTask;
        }, time, dialogs);
        return new Fixture(vm, sent, time);
    }

    [Fact]
    public void Build_SortsChannelsAndGroupsUsers()
    {
        var f = Create(others: [U(2, "zora", Lobby), U(3, "Anna", Lobby), U(4, "bob", Bravo)]);
        Assert.Equal(["Lobby", "alpha", "Bravo"], f.Vm.Channels.Select(c => c.Name));
        Assert.Equal(["Anna", "ich", "zora"], f.Channel(Lobby).Users.Select(u => u.Nickname));
        Assert.Equal(["bob"], f.Channel(Bravo).Users.Select(u => u.Nickname));
        Assert.True(f.Channel(Lobby).IsCurrent);
        Assert.True(f.User(1).IsSelf);
        Assert.Equal("Server", f.Vm.ServerName);
    }

    [Fact]
    public void CurrentChannelAndSelf_FollowOwnUser()
    {
        var f = Create();
        Assert.Equal(Lobby, f.Vm.CurrentChannel!.Id);
        Assert.Equal("ich", f.Vm.Self!.Nickname);

        f.Vm.Apply(new UserUpdated(U(1, "ich", Bravo)));
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
        var f = Create(others: U(2, "anna", Lobby));
        var anna = f.User(2);
        f.Vm.Apply(new UserUpdated(U(2, "anna", Alpha)));
        Assert.DoesNotContain(anna, f.Channel(Lobby).Users);
        Assert.Same(anna, Assert.Single(f.Channel(Alpha).Users));
    }

    [Fact]
    public void UserJoinedAndLeft_UpdateTree()
    {
        var f = Create();
        f.Vm.Apply(new UserJoined(U(5, "neu", Bravo)));
        Assert.Single(f.Channel(Bravo).Users);
        f.Vm.Apply(new UserLeft(5));
        Assert.Empty(f.Channel(Bravo).Users);
    }

    [Fact]
    public void Speaking_ClearsAfter300ms()
    {
        var f = Create(others: U(2, "anna", Lobby));
        f.Vm.OnSpeakers([new ActiveSpeaker(2, false)]);
        Assert.True(f.User(2).IsSpeaking);

        f.Time.Advance(TimeSpan.FromMilliseconds(200));
        f.Vm.RefreshSpeaking();
        Assert.True(f.User(2).IsSpeaking);

        f.Time.Advance(TimeSpan.FromMilliseconds(150));
        f.Vm.RefreshSpeaking();
        Assert.False(f.User(2).IsSpeaking);
    }

    [Fact]
    public void SpeakingViaLink_Flagged()
    {
        var f = Create(others: U(2, "anna", Alpha));
        f.Vm.OnSpeakers([new ActiveSpeaker(2, true)]);
        Assert.True(f.User(2).IsSpeakingViaLink);
        Assert.False(f.User(2).IsSpeaking);
    }

    [Fact]
    public void SelfTransmitting_ShowsOnOwnEntry()
    {
        var f = Create();
        f.Vm.SetSelfTransmitting(VoiceHeader.TargetLinked);
        Assert.True(f.User(1).IsSpeakingViaLink);
        f.Vm.SetSelfTransmitting(null);
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
        await f.Vm.ToggleDeafenCommand.ExecuteAsync(null);
        Assert.Equal(new SetSelfState(true, true), f.Sent[^1] with { RequestId = null });
        await f.Vm.ToggleDeafenCommand.ExecuteAsync(null);
        Assert.Equal(new SetSelfState(false, false), f.Sent[^1] with { RequestId = null });

        await f.Vm.ToggleMuteCommand.ExecuteAsync(null);
        await f.Vm.ToggleDeafenCommand.ExecuteAsync(null);
        await f.Vm.ToggleDeafenCommand.ExecuteAsync(null);
        Assert.True(f.Vm.SelfMuted); // was muted before deafening
        Assert.False(f.Vm.SelfDeafened);
    }

    [Fact]
    public void Error_BecomesGermanNotice()
    {
        var f = Create();
        string? notice = null;
        f.Vm.Notice += n => notice = n;
        f.Vm.Apply(new Error("r1", Codes.PermissionDenied));
        Assert.Equal(ErrorTexts.For(Codes.PermissionDenied), notice);
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
        var f = Create(Moderator, others: [U(2, "gast", Lobby), U(3, "admin", Lobby, P.All)]);
        var guest = f.User(2);
        Assert.True(guest.CanKick && guest.CanBan && guest.CanMute && guest.CanMove);

        var admin = f.User(3);
        Assert.False(admin.CanKick || admin.CanBan || admin.CanMute || admin.CanMove);

        var self = f.User(1);
        Assert.False(self.CanKick || self.CanBan || self.CanMute);
        Assert.True(self.CanMove);

        var plain = Create(others: U(2, "gast", Lobby));
        Assert.False(plain.User(2).CanKick || plain.User(2).CanMove);
    }

    [Fact]
    public void LinkCandidates_ExcludesSelfAndLinked()
    {
        var f = Create(P.All);
        f.Vm.Apply(new ChannelsLinked(Lobby, Alpha));
        Assert.Equal([Bravo], f.Vm.LinkCandidates(f.Channel(Lobby)).Select(c => c.Id));
        Assert.Equal([Alpha], f.Vm.LinkedChannelVms(f.Channel(Lobby)).Select(c => c.Id));
        Assert.True(f.Channel(Lobby).CanUnlink);
    }

    [Fact]
    public async Task LinkCommand_UsesPickedChannel()
    {
        var dialogs = new Dialogs { PickChannel = (_, candidates) => Task.FromResult<ChannelViewModel?>(candidates.Single(c => c.Name == "Bravo")) };
        var f = Create(P.All, dialogs: dialogs);
        await f.Channel(Lobby).LinkCommand.ExecuteAsync(null);
        Assert.Equal(new LinkChannels(Lobby, Bravo), f.Sent[^1] with { RequestId = null });
    }

    [Fact]
    public async Task CreateFromChannelMenu_UsesDialog()
    {
        var dialogs = new Dialogs { EditChannel = (_, _, _) => Task.FromResult<ChannelEdit?>(new ChannelEdit("Neu", "Beschreibung")) };
        var f = Create(P.All, dialogs: dialogs);
        await f.Channel(Alpha).CreateCommand.ExecuteAsync(null);
        Assert.Equal(new CreateChannel("Neu", "Beschreibung"), f.Sent[^1] with { RequestId = null });
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
        var dialogs = new Dialogs { Ban = _ => Task.FromResult<BanChoice?>(new BanChoice("spam", 60, true)) };
        var f = Create(Moderator, dialogs: dialogs, others: U(2, "gast", Lobby));
        await f.User(2).BanCommand.ExecuteAsync(null);
        Assert.Equal(new Ban(2, "spam", 60, true), f.Sent[^1] with { RequestId = null });
    }

    [Fact]
    public async Task CancelledDialog_SendsNothing()
    {
        var f = Create(P.All, others: U(2, "gast", Lobby));
        await f.User(2).KickCommand.ExecuteAsync(null);
        await f.Channel(Alpha).DeleteCommand.ExecuteAsync(null);
        await f.Vm.NewChannelCommand.ExecuteAsync(null);
        Assert.Empty(f.Sent);
    }
}
