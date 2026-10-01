using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Client.Views;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client;

/// <summary>Package 114: leaving a page or admin tab with unsaved changes asks first.</summary>
public sealed class LeaveGuardTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("ovs-leave-").FullName;
    readonly List<Request> sent = [];
    readonly Queue<LeaveChoice> answers = new();
    int asked;

    public void Dispose()
    {
        try
        {
            Directory.Delete(dir, true);
        }
        catch (IOException)
        {
        }
    }

    Dialogs Dialogs() => new()
    {
        AskLeave = () =>
        {
            asked++;
            return Task.FromResult(answers.Dequeue());
        },
    };

    MainViewModel Main(bool connected = true)
    {
        var dialogs = Dialogs();
        var vm = new MainViewModel(dir, a => a(), useAudioDevices: false) { Dialogs = dialogs };
        if (connected) vm.Server = FakeServers.Admin(sent, dialogs: dialogs);
        return vm;
    }

    static void Rename(AdminViewModel admin, string name) => admin.SelectedGroup!.Name = name;

    [AvaloniaFact]
    public void Settings_HasChanges_FalseUntouched_TrueAfterEdit_FalseAfterRevert()
    {
        var vm = new SettingsViewModel(new ClientSettings(), null, null);
        Assert.False(vm.HasChanges);
        var before = vm.VadThresholdDb;
        vm.VadThresholdDb = before - 5;
        Assert.True(vm.HasChanges);
        vm.VadThresholdDb = before;
        Assert.False(vm.HasChanges);
    }

    [AvaloniaFact]
    public async Task Admin_HasChanges_GroupDraftServerSettingsLinks()
    {
        var vm = Main();
        await vm.OpenAdminAsync();
        var admin = vm.AdminPage!;
        Assert.False(admin.HasAnyChanges);

        admin.SelectedGroup = admin.Groups.First(g => g.IsEditable);
        var name = admin.SelectedGroup.Name;
        Rename(admin, name + "2");
        Assert.True(admin.HasChanges(AdminViewModel.Place.Groups));
        Rename(admin, name);
        Assert.False(admin.HasChanges(AdminViewModel.Place.Groups));
        await admin.NewGroupCommand.ExecuteAsync(null);
        Assert.True(admin.HasChanges(AdminViewModel.Place.Groups)); // a new group is unsaved by itself
        admin.Discard(AdminViewModel.Place.Groups);
        Assert.False(admin.HasChanges(AdminViewModel.Place.Groups));

        admin.ServerName = "Neu";
        Assert.True(admin.HasChanges(AdminViewModel.Place.Server));
        admin.Discard(AdminViewModel.Place.Server);
        Assert.False(admin.HasChanges(AdminViewModel.Place.Server));

        foreach (var row in admin.Links.Rows) row.IsSelected = true;
        admin.Links.UnlinkSelectedCommand.Execute(null);
        Assert.True(admin.HasChanges(AdminViewModel.Place.Links));
        Assert.Equal(0, asked);
    }

    [AvaloniaFact]
    public async Task LeaveWithChanges_Asks_KeepEditingStays()
    {
        var vm = Main();
        vm.OpenSettings();
        vm.SettingsPage!.VadThresholdDb -= 5;

        answers.Enqueue(LeaveChoice.Stay);
        await vm.OpenAdminCommand.ExecuteAsync(null); // a page change
        Assert.Equal(1, asked);
        Assert.True(vm.IsSettingsPage);
        Assert.True(vm.SettingsPage!.HasChanges); // the edit is untouched

        answers.Enqueue(LeaveChoice.Stay);
        await vm.ClosePageAsync(); // Esc, the X
        Assert.True(vm.IsSettingsPage);

        // disconnecting keeps the settings page: nothing of it is lost, nothing is asked
        await vm.DisconnectCommand.ExecuteAsync(null);
        Assert.Equal(2, asked);
        Assert.Null(vm.Server);
    }

    [AvaloniaFact]
    public async Task Admin_Disconnect_AsksAndStays()
    {
        var vm = Main();
        await vm.OpenAdminAsync();
        vm.AdminPage!.ServerName = "Neu";
        answers.Enqueue(LeaveChoice.Stay);
        await vm.DisconnectCommand.ExecuteAsync(null);
        Assert.Equal(1, asked);
        Assert.NotNull(vm.Server);
        Assert.True(vm.IsAdminPage);
    }

    [AvaloniaFact]
    public async Task LeaveWithoutSaving_Discards()
    {
        var vm = Main();
        var saved = vm.Settings.VadThresholdDb;
        vm.OpenSettings();
        vm.SettingsPage!.VadThresholdDb = saved - 7;
        answers.Enqueue(LeaveChoice.Discard);
        await vm.OpenAdminCommand.ExecuteAsync(null);
        Assert.True(vm.IsAdminPage);
        Assert.Equal(saved, vm.Settings.VadThresholdDb);
    }

    [AvaloniaFact]
    public async Task SaveAndLeave_LeavesAfterConfirm_StaysOnRefusal()
    {
        // settings: saved, then the new page
        var vm = Main();
        var saved = vm.Settings.VadThresholdDb;
        vm.OpenSettings();
        vm.SettingsPage!.VadThresholdDb = saved - 7;
        answers.Enqueue(LeaveChoice.Save);
        await vm.OpenAdminCommand.ExecuteAsync(null);
        Assert.True(vm.IsAdminPage);
        Assert.Equal(saved - 7, vm.Settings.VadThresholdDb);

        // a group the server refuses: the page stays with its error
        var admin = vm.AdminPage!;
        admin.SelectedGroup = admin.Groups.First(g => g.IsEditable);
        Rename(admin, "Umbenannt");
        answers.Enqueue(LeaveChoice.Save);
        var leaving = vm.ClosePageAsync();
        var update = sent.OfType<UpdateGroup>().Last();
        vm.Server!.Apply(new Error(update.RequestId, Codes.PermissionDenied));
        await leaving;
        Assert.True(vm.IsAdminPage);
        Assert.True(admin.HasChanges(AdminViewModel.Place.Groups));

        // confirmed by the server's groups: the page goes
        answers.Enqueue(LeaveChoice.Save);
        leaving = vm.ClosePageAsync();
        update = sent.OfType<UpdateGroup>().Last();
        var groups = vm.Server.Mirror.Groups.Select(g => g.Id == update.GroupId ? g with { Name = update.Name } : g).ToList();
        vm.Server.Apply(new GroupsChanged(groups));
        await leaving;
        Assert.True(vm.IsHomePage);
    }

    [AvaloniaFact]
    public async Task CancelAndDiscardButtons_DoNotAsk()
    {
        var vm = Main();
        vm.OpenSettings();
        vm.SettingsPage!.VadThresholdDb -= 5;
        vm.SettingsPage.CancelCommand.Execute(null); // "Abbrechen"
        Assert.True(vm.IsHomePage);

        await vm.OpenAdminAsync();
        var links = vm.AdminPage!.Links;
        foreach (var row in links.Rows) row.IsSelected = true;
        links.UnlinkSelectedCommand.Execute(null);
        links.DiscardCommand.Execute(null); // "Verwerfen"
        Assert.False(vm.AdminPage.HasAnyChanges);
        Assert.Equal(0, asked);
    }

    [AvaloniaFact]
    public async Task WindowClose_WithChanges_AsksAndStaysOpen()
    {
        var vm = Main();
        var main = new MainWindow { DataContext = vm, Width = 1100, Height = 700 };
        main.Show();
        await vm.OpenAdminAsync();
        vm.AdminPage!.ServerName = "Neu";
        answers.Enqueue(LeaveChoice.Stay);
        main.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, asked);
        Assert.True(main.IsVisible);

        answers.Enqueue(LeaveChoice.Discard);
        main.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.False(main.IsVisible);
    }

    static AdminView Page(MainWindow main) => main.GetVisualDescendants().OfType<AdminView>().Single();

    [AvaloniaFact]
    public async Task AdminTabSwitch_WithChanges_AsksAndStays()
    {
        var vm = Main();
        var main = new MainWindow { DataContext = vm, Width = 1100, Height = 700 };
        main.Show();
        await vm.OpenAdminAsync();
        Dispatcher.UIThread.RunJobs();
        var page = Page(main);
        var tabs = page.FindControl<TabControl>("Tabs")!;
        var serverTab = page.FindControl<TabItem>("ServerTab")!;
        tabs.SelectedItem = serverTab;
        Dispatcher.UIThread.RunJobs();
        vm.AdminPage!.ServerName = "Neu";

        answers.Enqueue(LeaveChoice.Stay);
        tabs.SelectedItem = page.FindControl<TabItem>("GroupsTab");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, asked);
        Assert.Same(serverTab, tabs.SelectedItem);
        Assert.Equal("Neu", vm.AdminPage.ServerName);

        answers.Enqueue(LeaveChoice.Discard);
        tabs.SelectedItem = page.FindControl<TabItem>("GroupsTab");
        Dispatcher.UIThread.RunJobs();
        Assert.Same(page.FindControl<TabItem>("GroupsTab"), tabs.SelectedItem);
        Assert.Equal("Gilde", vm.AdminPage.ServerName); // discarded
        main.Close();
    }

    [AvaloniaFact]
    public async Task OtherGroup_WithChanges_AsksAndStays()
    {
        var vm = Main();
        var main = new MainWindow { DataContext = vm, Width = 1100, Height = 700 };
        main.Show();
        await vm.OpenAdminAsync();
        Dispatcher.UIThread.RunJobs();
        var list = Page(main).FindControl<ListBox>("GroupList")!;
        var admin = vm.AdminPage!;
        var edited = admin.Groups.First(g => g.IsEditable);
        list.SelectedItem = edited;
        Dispatcher.UIThread.RunJobs();
        Rename(admin, "Umbenannt");

        answers.Enqueue(LeaveChoice.Stay);
        list.SelectedItem = admin.Groups.First(g => g != edited);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, asked);
        Assert.Same(edited, admin.SelectedGroup);
        Assert.Equal("Umbenannt", edited.Name);

        answers.Enqueue(LeaveChoice.Discard);
        var other = admin.Groups.First(g => g != edited);
        list.SelectedItem = other;
        Dispatcher.UIThread.RunJobs();
        Assert.Same(other, admin.SelectedGroup);
        Assert.NotEqual("Umbenannt", edited.Name); // back to the server's name
        main.Close();
    }

    /// <summary>Found in review: "Speichern und verlassen" on a group switch saves and then goes to the clicked group.</summary>
    [AvaloniaFact]
    public async Task OtherGroup_SaveAndLeave_SavesThenSwitches()
    {
        var vm = Main();
        var main = new MainWindow { DataContext = vm, Width = 1100, Height = 700 };
        main.Show();
        await vm.OpenAdminAsync();
        Dispatcher.UIThread.RunJobs();
        var list = Page(main).FindControl<ListBox>("GroupList")!;
        var admin = vm.AdminPage!;
        var edited = admin.Groups.First(g => g.IsEditable);
        list.SelectedItem = edited;
        Dispatcher.UIThread.RunJobs();
        Rename(admin, "Umbenannt");

        answers.Enqueue(LeaveChoice.Save);
        var other = admin.Groups.First(g => g != edited);
        list.SelectedItem = other;
        Dispatcher.UIThread.RunJobs();
        var update = sent.OfType<UpdateGroup>().Single();
        vm.Server!.Apply(new GroupsChanged(vm.Server.Mirror.Groups.Select(g => g.Id == update.GroupId ? g with { Name = update.Name } : g).ToList()));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(other.Id, admin.SelectedGroup!.Id);
        Assert.False(admin.HasAnyChanges);
        main.Close();
    }

    /// <summary>Found in review: any change on the server rebuilt the group rows and dropped the edit without a word.</summary>
    [AvaloniaFact]
    public async Task GroupEdit_SurvivesServerChanges()
    {
        var vm = Main();
        await vm.OpenAdminAsync();
        var admin = vm.AdminPage!;
        admin.SelectedGroup = admin.Groups.First(g => g.IsEditable);
        Rename(admin, "Umbenannt");
        var self = vm.Server!.Mirror.Users[vm.Server.Mirror.SelfId];
        vm.Server.Apply(new UserUpdated(self with { SelfMuted = !self.SelfMuted })); // something else changes
        Assert.Equal("Umbenannt", admin.SelectedGroup!.Name);
        Assert.True(admin.HasChanges(AdminViewModel.Place.Groups));
    }

    /// <summary>Found in review: the server keeps line breaks as LF and a trimmed name; a saved edit counts as saved, and untouched fields follow another admin.</summary>
    [AvaloniaFact]
    public async Task ServerTab_SavedEditIsNoChange_UntouchedFollowsServer()
    {
        var vm = Main();
        await vm.OpenAdminAsync();
        var admin = vm.AdminPage!;
        var settings = vm.Server!.Mirror.Settings;
        vm.Server.Apply(new ServerSettingsChanged(settings with { Name = "Anderer Name" })); // another admin
        Assert.Equal("Anderer Name", admin.ServerName);
        Assert.False(admin.HasAnyChanges);

        admin.ServerName = "Neu ";
        admin.WelcomeText = "Zeile 1" + (char)13 + (char)10 + "Zeile 2";
        Assert.True(admin.HasChanges(AdminViewModel.Place.Server));
        vm.Server.Apply(new ServerSettingsChanged(vm.Server.Mirror.Settings with { Name = "Neu", WelcomeText = "Zeile 1" + (char)10 + "Zeile 2" }));
        Assert.False(admin.HasChanges(AdminViewModel.Place.Server));
    }

    /// <summary>Found in review: sliders turn floats into percent and back, which is not exact; opening is no change.</summary>
    [AvaloniaFact]
    public void Settings_SliderRoundTrip_IsNoChange()
    {
        var random = new Random(7);
        for (int i = 0; i < 200; i++)
        {
            var current = new ClientSettings { InputGain = (float)(random.NextDouble() * 2), OutputVolume = (float)random.NextDouble() };
            Assert.False(new SettingsViewModel(current, null, null).HasChanges, $"{current.InputGain} {current.OutputVolume}");
        }
        var missing = new SettingsViewModel(new ClientSettings { InputDeviceId = "weg" }, null, null);
        missing.ShowDevices([], []); // the saved device is gone: the fallback is no change of the user's
        Assert.False(missing.HasChanges);
    }

    /// <summary>Found in the second review: a device list arriving with the page bound cleared the selection and crashed the change check.</summary>
    [AvaloniaFact]
    public void Settings_DeviceListArrives_WithViewBound_NoCrashNoChange()
    {
        new ClientSettings { InputDeviceId = "weg", OutputDeviceId = "b" }.Save(dir);
        var vm = Main(connected: false);
        var main = new MainWindow { DataContext = vm, Width = 1100, Height = 700 };
        main.Show();
        vm.OpenSettings();
        Dispatcher.UIThread.RunJobs();
        vm.SettingsPage!.ShowDevices([new OVS.Client.Audio.AudioDevice("a", "Mikrofon")], [new OVS.Client.Audio.AudioDevice("b", "Lautsprecher")]);
        Dispatcher.UIThread.RunJobs();
        Assert.False(vm.SettingsPage.HasChanges);
        main.Close();
    }

    /// <summary>Found in the second review: new settings from the server kept a typed password, and follow field by field.</summary>
    [AvaloniaFact]
    public async Task ServerTab_ArrivingSettings_KeepPasswordAndFollowPerField()
    {
        var vm = Main();
        await vm.OpenAdminAsync();
        var admin = vm.AdminPage!;
        admin.NewPassword = "geheim";
        admin.ServerName = "Mein Name";
        vm.Server!.Apply(new ServerSettingsChanged(vm.Server.Mirror.Settings with { WelcomeText = "Neu von jemand anderem" }));
        Assert.Equal("geheim", admin.NewPassword);
        Assert.Equal("Mein Name", admin.ServerName); // the edit stays
        Assert.Equal("Neu von jemand anderem", admin.WelcomeText); // the untouched field follows
        Assert.True(admin.HasChanges(AdminViewModel.Place.Server));
    }

    /// <summary>Found in the second review: an edited row built for rights the user no longer has is rebuilt for the new ones.</summary>
    [AvaloniaFact]
    public async Task GroupEdit_RightsLost_RowFollowsRights()
    {
        var vm = Main();
        await vm.OpenAdminAsync();
        var admin = vm.AdminPage!;
        admin.SelectedGroup = admin.Groups.First(g => g.IsEditable && g.Id != WellKnownGroups.Admin);
        var id = admin.SelectedGroup.Id;
        Rename(admin, "Umbenannt");
        var self = vm.Server!.Mirror.Users[vm.Server.Mirror.SelfId];
        vm.Server.Apply(new UserUpdated(self with { Permissions = Permission.All & ~Permission.GroupsManage }));
        var row = admin.Groups.Single(g => g.Id == id);
        Assert.True(row.IsReadOnly);
        Assert.False(admin.HasChanges(AdminViewModel.Place.Groups)); // nothing that could still be saved
    }

    /// <summary>Found in the third review: an edited group follows another admin in what the user did not touch, so a save keeps both.</summary>
    [AvaloniaFact]
    public async Task GroupEdit_FollowsOtherAdminInUntouchedFields()
    {
        var vm = Main();
        await vm.OpenAdminAsync();
        var admin = vm.AdminPage!;
        var row = admin.Groups.First(g => g.IsEditable && g.Id == WellKnownGroups.Guest);
        admin.SelectedGroup = row;
        Rename(admin, "Besucher");
        var guest = vm.Server!.Mirror.Groups.Single(g => g.Id == WellKnownGroups.Guest);
        vm.Server.Apply(new GroupsChanged(vm.Server.Mirror.Groups
            .Select(g => g.Id == WellKnownGroups.Guest ? g with { Permissions = g.Permissions | Permission.ChatServer } : g).ToList()));
        var shown = admin.Groups.Single(g => g.Id == WellKnownGroups.Guest);
        Assert.Equal("Besucher", shown.Name); // the edit stays
        Assert.True(shown.Permissions.Has(Permission.ChatServer)); // the other admin s change is shown too
        Assert.True(admin.HasChanges(AdminViewModel.Place.Groups));
        Assert.Equal(guest.Permissions | Permission.ChatServer, shown.Permissions);
    }
}
