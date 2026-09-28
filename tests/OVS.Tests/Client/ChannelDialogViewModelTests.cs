using OVS.Client.ViewModels;
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
}
