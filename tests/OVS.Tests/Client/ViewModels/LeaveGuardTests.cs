using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OVS.Client.Dialogs;
using OVS.Client.Models;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Client.Views;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.Groups;
using OVS.Shared.Protocol.Messages.ServerInfo;
using OVS.Shared.Protocol.Messages.Users;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.ViewModels;

/// <summary>Package 114: leaving a page or admin tab with unsaved changes asks first.</summary>
[Collection(MotionCollection.Name)]
public sealed class LeaveGuardTests() : TempDirectoryTest("ovs-leave-", quiet: true)
{
    readonly List<Request> sent = [];
    readonly Queue<LeaveChoice> answers = new();
    int asked;

    DialogCallbacks Dialogs() => new()
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
        var vm = MainViewModelSetup.Create(dir, a => a(), dialogs: dialogs);
        if (connected) MainViewModelSetup.ShowServer(vm, FakeServers.Admin(sent, dialogs: dialogs));
        return vm;
    }

    static void Rename(AdminViewModel admin, string name) => admin.GroupList.SelectedGroup!.Name = name;

    [AvaloniaFact]
    public void Settings_HasChanges_FalseUntouched_TrueAfterEdit_FalseAfterRevert()
    {
        var vm = new SettingsViewModel(new ClientSettings(), null, null, TimeProvider.System);
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

        admin.GroupList.SelectedGroup = admin.GroupList.Groups.First(g => g.IsEditable);
        var name = admin.GroupList.SelectedGroup.Name;
        Rename(admin, name + "2");
        Assert.True(admin.GroupList.Place.HasChanges());
        Rename(admin, name);
        Assert.False(admin.GroupList.Place.HasChanges());
        await admin.GroupList.NewGroupCommand.ExecuteAsync(null);
        Assert.True(admin.GroupList.Place.HasChanges()); // a new group is unsaved by itself
        admin.GroupList.Place.Discard();
        Assert.False(admin.GroupList.Place.HasChanges());

        admin.ServerSettings.Name = "Neu";
        Assert.True(admin.ServerSettings.Place.HasChanges());
        admin.ServerSettings.Place.Discard();
        Assert.False(admin.ServerSettings.Place.HasChanges());

        admin.Links.UnlinkAll();
        Assert.True(admin.Links.Place.HasChanges());
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
        vm.AdminPage!.ServerSettings.Name = "Neu";
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
        admin.GroupList.SelectedGroup = admin.GroupList.Groups.First(g => g.IsEditable);
        Rename(admin, "Umbenannt");
        answers.Enqueue(LeaveChoice.Save);
        var leaving = vm.ClosePageAsync();
        var update = sent.OfType<UpdateGroup>().Last();
        vm.Server!.Apply(new Error(update.RequestId, ReasonCodes.PermissionDenied));
        await leaving;
        Assert.True(vm.IsAdminPage);
        Assert.True(admin.GroupList.Place.HasChanges());

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
        links.UnlinkAll();
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
        vm.AdminPage!.ServerSettings.Name = "Neu";
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
        vm.AdminPage!.ServerSettings.Name = "Neu";

        answers.Enqueue(LeaveChoice.Stay);
        tabs.SelectedItem = page.FindControl<TabItem>("GroupsTab");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, asked);
        Assert.Same(serverTab, tabs.SelectedItem);
        Assert.Equal("Neu", vm.AdminPage.ServerSettings.Name);

        answers.Enqueue(LeaveChoice.Discard);
        tabs.SelectedItem = page.FindControl<TabItem>("GroupsTab");
        Dispatcher.UIThread.RunJobs();
        Assert.Same(page.FindControl<TabItem>("GroupsTab"), tabs.SelectedItem);
        Assert.Equal("Gilde", vm.AdminPage.ServerSettings.Name); // discarded
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
        var list = Page(main).FindControl<ListBox>("GroupRows")!;
        var admin = vm.AdminPage!;
        var edited = admin.GroupList.Groups.First(g => g.IsEditable);
        list.SelectedItem = edited;
        Dispatcher.UIThread.RunJobs();
        Rename(admin, "Umbenannt");

        answers.Enqueue(LeaveChoice.Stay);
        list.SelectedItem = admin.GroupList.Groups.First(g => g != edited);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, asked);
        Assert.Same(edited, admin.GroupList.SelectedGroup);
        Assert.Equal("Umbenannt", edited.Name);

        answers.Enqueue(LeaveChoice.Discard);
        var other = admin.GroupList.Groups.First(g => g != edited);
        list.SelectedItem = other;
        Dispatcher.UIThread.RunJobs();
        Assert.Same(other, admin.GroupList.SelectedGroup);
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
        var list = Page(main).FindControl<ListBox>("GroupRows")!;
        var admin = vm.AdminPage!;
        var edited = admin.GroupList.Groups.First(g => g.IsEditable);
        list.SelectedItem = edited;
        Dispatcher.UIThread.RunJobs();
        Rename(admin, "Umbenannt");

        answers.Enqueue(LeaveChoice.Save);
        var other = admin.GroupList.Groups.First(g => g != edited);
        list.SelectedItem = other;
        Dispatcher.UIThread.RunJobs();
        var update = sent.OfType<UpdateGroup>().Single();
        vm.Server!.Apply(new GroupsChanged(vm.Server.Mirror.Groups.Select(g => g.Id == update.GroupId ? g with { Name = update.Name } : g).ToList()));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(other.Id, admin.GroupList.SelectedGroup!.Id);
        Assert.False(admin.HasAnyChanges);
        main.Close();
    }

    /// <summary>The page hands its leave question to the groups tab: a new group waits for the answer for the edited group.</summary>
    [AvaloniaFact]
    public async Task NewGroup_GroupEditedAndUserStays_AsksOnceAddsNoDraftAndKeepsTheEdit()
    {
        var vm = Main();
        await vm.OpenAdminAsync();
        var admin = vm.AdminPage!;
        admin.GroupList.SelectedGroup = admin.GroupList.Groups.First(g => g.IsEditable);
        Rename(admin, "Umbenannt");
        answers.Enqueue(LeaveChoice.Stay);

        await HangGuard.Bound(admin.GroupList.NewGroupCommand.ExecuteAsync(null));

        Assert.Equal(1, asked);
        Assert.DoesNotContain(admin.GroupList.Groups, g => g.Id is null);
        Assert.Equal("Umbenannt", admin.GroupList.SelectedGroup!.Name);
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
        Assert.Equal("Anderer Name", admin.ServerSettings.Name);
        Assert.False(admin.HasAnyChanges);

        admin.ServerSettings.Name = "Neu ";
        admin.ServerSettings.WelcomeText = "Zeile 1" + (char)13 + (char)10 + "Zeile 2";
        Assert.True(admin.ServerSettings.Place.HasChanges());
        vm.Server.Apply(new ServerSettingsChanged(vm.Server.Mirror.Settings with { Name = "Neu", WelcomeText = "Zeile 1" + (char)10 + "Zeile 2" }));
        Assert.False(admin.ServerSettings.Place.HasChanges());
    }

    /// <summary>Found in review: sliders turn floats into percent and back, which is not exact; opening is no change.</summary>
    [AvaloniaFact]
    public void Settings_SliderRoundTrip_IsNoChange()
    {
        var random = new Random(7);
        for (int i = 0; i < 200; i++)
        {
            var current = new ClientSettings { InputGain = (float)(random.NextDouble() * 2), OutputVolume = (float)random.NextDouble() };
            Assert.False(new SettingsViewModel(current, null, null, TimeProvider.System).HasChanges, $"{current.InputGain} {current.OutputVolume}");
        }
        var missing = new SettingsViewModel(new ClientSettings { InputDeviceId = "weg" }, null, null, TimeProvider.System);
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
        vm.SettingsPage!.ShowDevices([new OVS.Client.Audio.Devices.AudioDevice("a", "Mikrofon")], [new OVS.Client.Audio.Devices.AudioDevice("b", "Lautsprecher")]);
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
        admin.ServerSettings.NewPassword = "geheim";
        admin.ServerSettings.Name = "Mein Name";
        vm.Server!.Apply(new ServerSettingsChanged(vm.Server.Mirror.Settings with { WelcomeText = "Neu von jemand anderem" }));
        Assert.Equal("geheim", admin.ServerSettings.NewPassword);
        Assert.Equal("Mein Name", admin.ServerSettings.Name); // the edit stays
        Assert.Equal("Neu von jemand anderem", admin.ServerSettings.WelcomeText); // the untouched field follows
        Assert.True(admin.ServerSettings.Place.HasChanges());
    }
}
