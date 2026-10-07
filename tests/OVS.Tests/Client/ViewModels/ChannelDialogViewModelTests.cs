using OVS.Client.Dialogs;
using OVS.Client.Localization;
using OVS.Client.ViewModels;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Groups;

namespace OVS.Tests.Client.ViewModels;

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
        Assert.Equal(ProtocolLimits.MaxChannelUsers, vm.MaxUsers);
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

    /// <summary>Package 94: a new password, "Passwort entfernen", or nothing (unchanged); never on the default channel.</summary>
    [Fact]
    public void Password_SetRemoveUnchanged_DisabledForDefault()
    {
        var vm = new ChannelDialogViewModel(new ChannelEdit("Raid", "", HasPassword: true), ChannelDialogMode.Edit);
        Assert.True(vm.HasPassword);
        Assert.True(vm.CanEnterPassword);
        Assert.Null(vm.Result()!.Password);
        vm.Password = "neu";
        Assert.Equal("neu", vm.Result()!.Password);
        vm.RemovePassword = true;
        Assert.False(vm.CanEnterPassword);
        Assert.Equal("", vm.Result()!.Password);

        var create = new ChannelDialogViewModel(new ChannelEdit("", ""), ChannelDialogMode.Create) { Name = "Neu" };
        Assert.False(create.HasPassword);
        Assert.Null(create.Result()!.Password);
        create.Password = new string('x', ProtocolLimits.MaxPasswordLength + 1);
        Assert.Null(create.Result());
        Assert.Equal(Strings.Dlg_PasswordTooLong, create.Error);

        var lobby = new ChannelDialogViewModel(new ChannelEdit("Lobby", ""), ChannelDialogMode.EditDefault) { Password = "x" };
        Assert.False(lobby.CanEnterPassword);
        Assert.Null(lobby.Result()!.Password);
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

    /// <summary>Package 132 (A141): "Qualität" starts at 64 kbps for a new channel and at the channel's own when editing, bounded 16 to 128.</summary>
    [Fact]
    public void Bitrate_ShownAndEdited()
    {
        var create = new ChannelDialogViewModel(new ChannelEdit("", ""), ChannelDialogMode.Create) { Name = "Neu" };
        Assert.Equal(64m, create.Bitrate);
        Assert.Equal((16m, 128m), (create.MinBitrate, create.MaxBitrate));
        Assert.Equal(64, create.Result()!.Bitrate);
        create.Bitrate = 96;
        Assert.Equal(new ChannelEdit("Neu", "", Bitrate: 96), create.Result());

        var edit = new ChannelDialogViewModel(new ChannelEdit("Raid", "", Bitrate: 32), ChannelDialogMode.Edit);
        Assert.Equal(32m, edit.Bitrate);
        Assert.Equal(32, edit.Result()!.Bitrate);
        edit.Bitrate = 500;
        Assert.Equal(128m, edit.Bitrate);
        edit.Bitrate = 3;
        Assert.Equal(16m, edit.Bitrate);
        edit.Bitrate = null; // an emptied number field keeps the channel's own
        Assert.Equal(32, edit.Result()!.Bitrate);

        // the default channel has its own quality too
        var lobby = new ChannelDialogViewModel(new ChannelEdit("Lobby", "", Bitrate: 48), ChannelDialogMode.EditDefault) { Bitrate = 80 };
        Assert.Equal(80, lobby.Result()!.Bitrate);
        // a separator has none
        Assert.Equal(64, new ChannelDialogViewModel(new ChannelEdit("", ""), ChannelDialogMode.Create) { IsSeparator = true, Bitrate = 96 }.Result()!.Bitrate);
    }

    /// <summary>Package 132: the channel's bitrate goes into the dialog and the edited one to the server.</summary>
    [Fact]
    public async Task Bitrate_FromChannelIntoDialog_SentWithEdit()
    {
        ChannelEdit? shown = null;
        var sent = new List<Request>();
        var server = OVS.Tests.TestSupport.FakeServers.Admin(sent, dialogs: new DialogCallbacks
        {
            EditChannel = (current, _, _) =>
            {
                shown = current;
                return Task.FromResult<ChannelEdit?>(current with { Bitrate = 128 });
            },
        });
        var raid = OVS.Tests.TestSupport.FakeServers.Raid;
        server.Apply(new ChannelUpdated(new ChannelInfo(raid, "Raid", "", 1, Bitrate: 24)));
        var channel = server.Channels.Single(c => c.Id == raid);
        Assert.Equal(24, channel.Bitrate);
        await channel.EditCommand.ExecuteAsync(null);
        Assert.Equal(24, shown!.Bitrate);
        Assert.Equal(128, Assert.IsType<EditChannel>(sent[^1]).Bitrate);

        await channel.CreateCommand.ExecuteAsync(null); // the dialog answers with a 128 kbps channel
        Assert.Equal((64, 128), (shown.Bitrate, Assert.IsType<CreateChannel>(sent[^1]).Bitrate));
    }

    /// <summary>Package 111: creating, "Trenner" hides every field and gives a separator without name or options.</summary>
    [Fact]
    public void Create_Separator_HidesFieldsAndSendsKind()
    {
        var vm = new ChannelDialogViewModel(new ChannelEdit("", ""), ChannelDialogMode.Create);
        Assert.True(vm.CanChooseKind);
        Assert.True(vm.ShowsChannelFields);
        Assert.Null(vm.Result()); // a voice channel still needs its name
        vm.IsSeparator = true;
        Assert.False(vm.ShowsChannelFields);
        Assert.Equal(new ChannelEdit("", "", Kind: ChannelKind.Separator), vm.Result());
        Assert.False(new ChannelDialogViewModel(new ChannelEdit("Raid", ""), ChannelDialogMode.Edit).CanChooseKind);
    }
}
