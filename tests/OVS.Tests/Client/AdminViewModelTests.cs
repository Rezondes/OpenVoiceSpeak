using OVS.Client.Net;
using OVS.Client.ViewModels;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Tests.TestSupport;
using P = OVS.Shared.Permissions.Permission;

namespace OVS.Tests.Client;

public class AdminViewModelTests
{
    static readonly Guid Lobby = Guid.NewGuid();
    static readonly Guid ModGroup = Guid.NewGuid();

    static (AdminViewModel Admin, ServerViewModel Server, List<Request> Sent) Create(P selfPerms, bool hasPassword = false)
    {
        var snapshot = new ServerSnapshot(new ServerSettingsInfo("Server", "Hallo", hasPassword), Lobby,
            [new ChannelInfo(Lobby, "Lobby", "", 0)], [],
            [
                new GroupInfo(WellKnownGroups.Guest, "Gast", P.Speak),
                new GroupInfo(ModGroup, "Moderator", P.Speak | P.UserKick),
                new GroupInfo(WellKnownGroups.Admin, "Admin", P.All),
            ],
            [new UserInfo(1, "fp1", "ich", Lobby, false, false, false, selfPerms, [])]);
        var sent = new List<Request>();
        var server = new ServerViewModel(new StateMirror(new Welcome(1, "", snapshot)), r =>
        {
            sent.Add(r);
            return Task.CompletedTask;
        }, new ManualTimeProvider());
        return (new AdminViewModel(server), server, sent);
    }

    static GroupEditViewModel Group(AdminViewModel vm, string name) => vm.Groups.Single(g => g.Name == name);

    [Fact]
    public void GroupEditor_DisablesUnownedPermissions()
    {
        var (vm, _, _) = Create(P.GroupsManage | P.Speak | P.UserKick);
        var mod = Group(vm, "Moderator");
        Assert.True(mod.Toggles.Single(t => t.Permission == P.UserKick).IsEnabled);
        Assert.False(mod.Toggles.Single(t => t.Permission == P.ServerConfig).IsEnabled);
        Assert.Equal(P.Speak | P.UserKick, mod.Permissions);
        Assert.True(mod.CanDelete);
    }

    [Fact]
    public void AdminGroup_ReadOnly_ProtectedGroupsNotDeletable()
    {
        var (vm, _, _) = Create(P.All);
        var admin = Group(vm, "Admin");
        Assert.True(admin.IsReadOnly);
        Assert.All(admin.Toggles, t => Assert.False(t.IsEnabled));
        Assert.False(admin.CanDelete);
        Assert.False(Group(vm, "Gast").CanDelete);
    }

    [Fact]
    public void GroupButtons_DisabledForProtectedAndStrongerGroups()
    {
        var (vm, _, _) = Create(P.GroupsManage | P.Speak);
        vm.SelectedGroup = Group(vm, "Admin");
        Assert.False(vm.SaveGroupCommand.CanExecute(null));
        Assert.False(vm.DeleteGroupCommand.CanExecute(null));

        vm.SelectedGroup = Group(vm, "Gast");
        Assert.True(vm.SaveGroupCommand.CanExecute(null));
        Assert.False(vm.DeleteGroupCommand.CanExecute(null));

        // Moderator can kick, the actor cannot: not editable, not deletable
        vm.SelectedGroup = Group(vm, "Moderator");
        Assert.True(vm.SelectedGroup.IsReadOnly);
        Assert.False(vm.SaveGroupCommand.CanExecute(null));
        Assert.False(vm.DeleteGroupCommand.CanExecute(null));
    }

    [Fact]
    public void BanDurations_AsPlanned()
    {
        Assert.Equal([("1 Stunde", (int?)60), ("1 Tag", 1440), ("7 Tage", 10080), ("Dauerhaft", null)], BanChoice.Durations);
    }

    [Theory]
    [InlineData(P.GroupsManage, true, false, false, false)]
    [InlineData(P.GroupsAssign, false, true, false, false)]
    [InlineData(P.UserBan, false, false, true, false)]
    [InlineData(P.ServerConfig, false, false, false, true)]
    public void Tabs_VisibleByPermission(P perms, bool groups, bool users, bool bans, bool server)
    {
        var (vm, _, _) = Create(perms);
        Assert.Equal((groups, users, bans, server), (vm.ShowGroups, vm.ShowUsers, vm.ShowBans, vm.ShowServer));
    }

    [Fact]
    public async Task RequestLists_OnlyAllowedOnes()
    {
        var (vm, _, sent) = Create(P.UserBan);
        await vm.RequestListsAsync();
        Assert.IsType<ListBans>(Assert.Single(sent));
    }

    [Fact]
    public async Task AssignGroup_SendsRequest_DisablesHigherGroups()
    {
        var (vm, server, sent) = Create(P.GroupsAssign | P.Speak);
        server.Apply(new UserList("r", [new KnownUserInfo("fpX", "Xaver", [WellKnownGroups.Guest])]));

        var user = Assert.Single(vm.Users);
        var guest = user.Toggles.Single(t => t.Name == "Gast");
        Assert.True(guest.IsChecked);
        Assert.True(guest.IsEnabled);
        Assert.False(user.Toggles.Single(t => t.Name == "Admin").IsEnabled);
        Assert.False(user.Toggles.Single(t => t.Name == "Moderator").IsEnabled);

        guest.IsChecked = false; // what the checkbox does before running its command
        await guest.ToggleCommand.ExecuteAsync(null);
        Assert.Equal(new UnassignGroup("fpX", WellKnownGroups.Guest), sent[^2] with { RequestId = null });
        Assert.IsType<ListUsers>(sent[^1]);
    }

    [Fact]
    public void BanList_ShowsBans_UnbanSends()
    {
        var (vm, server, sent) = Create(P.UserBan);
        var ban = new BanInfo(Guid.NewGuid(), "abcdef0123456789", "troll", "1.2.3.4", "spam", "mod", null);
        server.Apply(new BanList("r", [ban]));
        var entry = Assert.Single(vm.Bans);
        Assert.Contains("troll", entry.Text);
        Assert.Contains("dauerhaft", entry.Text);
        entry.UnbanCommand.Execute(null);
        Assert.Equal(new Unban(ban.Id), sent[^1] with { RequestId = null });
    }

    [Fact]
    public async Task NewGroup_SaveSendsCreate()
    {
        var (vm, _, sent) = Create(P.All);
        vm.NewGroupCommand.Execute(null);
        vm.SelectedGroup!.Name = "Team";
        vm.SelectedGroup.Toggles.Single(t => t.Permission == P.SpeakLinked).IsChecked = true;
        await vm.SaveGroupCommand.ExecuteAsync(null);
        Assert.Equal(new CreateGroup("Team", P.Speak | P.SpeakLinked), sent[^1] with { RequestId = null });
    }

    [Theory]
    [InlineData("", false, null)]
    [InlineData("neu", false, "neu")]
    [InlineData("egal", true, "")]
    public async Task SaveServerSettings_PasswordSemantics(string newPassword, bool remove, string? expected)
    {
        var (vm, _, sent) = Create(P.ServerConfig, hasPassword: true);
        vm.ServerName = "Neu";
        vm.NewPassword = newPassword;
        vm.RemovePassword = remove;
        await vm.SaveServerSettingsCommand.ExecuteAsync(null);
        Assert.Equal(new UpdateServerSettings("Neu", "Hallo", expected), sent[^1] with { RequestId = null });
    }
}
