using OVS.Client.Localization;
using OVS.Client.ViewModels;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;

namespace OVS.Tests.Client;

/// <summary>Package 54: one dialog for creating and editing a channel.</summary>
public class ChannelDialogViewModelTests
{
    [Fact]
    public void Create_EmptyDefaults()
    {
        var vm = new ChannelDialogViewModel(new ChannelEdit("", ""), ChannelDialogMode.Create);
        Assert.Equal(("Channel anlegen", "Anlegen"), (vm.Title, vm.ConfirmText));
        Assert.Equal(("", "", false, 0m, true), (vm.Name, vm.Description, vm.IsMuted, vm.MaxUsers, vm.CanLimitUsers));
        Assert.Equal("Wer drin ist, bleibt auch bei einem kleineren Limit. Das Recht \"Volle Channel betreten\" umgeht es.", vm.SlotsHint);
    }

    [Fact]
    public void Edit_Prefilled()
    {
        var vm = new ChannelDialogViewModel(new ChannelEdit("Raid", "Nur Raid", IsMuted: true, MaxUsers: 10), ChannelDialogMode.Edit);
        Assert.Equal(("Channel bearbeiten", "Speichern"), (vm.Title, vm.ConfirmText));
        Assert.Equal(("Raid", "Nur Raid", true, 10m, true), (vm.Name, vm.Description, vm.IsMuted, vm.MaxUsers, vm.CanLimitUsers));
        Assert.Equal(new ChannelEdit("Raid", "Nur Raid", true, 10), vm.Result());
    }

    [Fact]
    public void Default_SlotsLocked()
    {
        var vm = new ChannelDialogViewModel(new ChannelEdit("Lobby", ""), ChannelDialogMode.EditDefault);
        Assert.False(vm.CanLimitUsers);
        Assert.Contains("Standard-Channel", vm.SlotsHint);
        vm.MaxUsers = 5; // cannot happen through the locked field, but never sent either
        Assert.Equal(0, vm.Result()!.MaxUsers);
    }

    [Fact]
    public void NameRequired_SlotsBounded_Trimmed()
    {
        var vm = new ChannelDialogViewModel(new ChannelEdit("", ""), ChannelDialogMode.Create);
        Assert.Null(vm.Result());
        vm.Name = "   ";
        Assert.Null(vm.Result());
        vm.Name = "  Raid  ";
        vm.MaxUsers = 5000;
        Assert.Equal(ProtocolInfo.MaxChannelUsers, vm.MaxUsers);
        vm.MaxUsers = -3;
        Assert.Equal(0m, vm.MaxUsers);
        vm.MaxUsers = null; // an emptied number field
        Assert.Equal(new ChannelEdit("Raid", "", false, 0), vm.Result());
    }

    /// <summary>Package 83: the server's name and text rules, shown before sending.</summary>
    [Fact]
    public void InvalidNameOrDescription_ErrorShown()
    {
        var vm = new ChannelDialogViewModel(new ChannelEdit("", ""), ChannelDialogMode.Create);
        vm.Name = "Raid" + (char)0x202E; // a bidi override
        Assert.Null(vm.Result());
        Assert.Equal(Strings.Dlg_NameInvalid, vm.Error);

        vm.Name = "Raid";
        vm.Description = "a" + (char)7 + "b";
        Assert.Null(vm.Result());
        Assert.Equal(Strings.Dlg_TextInvalid, vm.Error);

        vm.Description = "oben\r\nunten"; // a line break from the text box is fine
        Assert.Equal(new ChannelEdit("Raid", "oben\r\nunten", false, 0), vm.Result());
        Assert.Null(vm.Error);
    }

    static readonly Guid Mod = Guid.NewGuid(), RaidGroup = Guid.NewGuid();
    static readonly GroupInfo[] Groups =
    [
        new(WellKnownGroups.Guest, "Gast", Permission.None), new(Mod, "Moderator", Permission.None),
        new(RaidGroup, "Raid", Permission.None), new(WellKnownGroups.Admin, "Admin", Permission.None),
    ];

    /// <summary>Package 93: a checkbox per group; the list is only sent when it changed (null keeps the lock).</summary>
    [Fact]
    public void GroupLock_ListedChecked_SentOnlyWhenChanged()
    {
        var vm = new ChannelDialogViewModel(new ChannelEdit("Raid", "", AllowedGroupIds: [RaidGroup]), ChannelDialogMode.Edit, Groups);
        Assert.Equal(["Gast", "Moderator", "Raid", "Admin"], vm.Groups.Select(g => g.Name));
        Assert.Equal([RaidGroup], vm.Groups.Where(g => g.IsChecked).Select(g => g.Id));
        Assert.True(vm.CanLockGroups);
        Assert.Null(vm.Result()!.AllowedGroupIds);

        vm.Groups[1].IsChecked = true;
        Assert.Equal([Mod, RaidGroup], vm.Result()!.AllowedGroupIds!);
        vm.Groups[1].IsChecked = false;
        vm.Groups[2].IsChecked = false;
        Assert.Equal([], vm.Result()!.AllowedGroupIds!); // the lock goes

        var create = new ChannelDialogViewModel(new ChannelEdit("", ""), ChannelDialogMode.Create, Groups) { Name = "Neu" };
        Assert.Null(create.Result()!.AllowedGroupIds);
        create.Groups[1].IsChecked = true;
        Assert.Equal([Mod], create.Result()!.AllowedGroupIds!);
    }

    [Fact]
    public void GroupLock_DisabledForDefault()
    {
        var vm = new ChannelDialogViewModel(new ChannelEdit("Lobby", ""), ChannelDialogMode.EditDefault, Groups);
        Assert.False(vm.CanLockGroups);
        Assert.Contains("Standard-Channel", vm.GroupLockHint);
        vm.Groups[1].IsChecked = true; // cannot happen through the disabled list, but never sent either
        Assert.Null(vm.Result()!.AllowedGroupIds);
    }
}
