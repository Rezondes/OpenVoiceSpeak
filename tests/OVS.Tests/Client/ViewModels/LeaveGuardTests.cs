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

    /// <summary>The page flow and the administration page ask the same question.</summary>
    DialogCallbacks Dialogs() => new() { PageFlow = new() { AskLeave = AskLeaveAsync }, Admin = new() { AskLeave = AskLeaveAsync } };

    Task<LeaveChoice> AskLeaveAsync()
    {
        asked++;
        order.Add("question");
        return Task.FromResult(answers.Dequeue());
    }

    MainViewModel Main(bool connected = true)
    {
        var dialogs = Dialogs();
        // posts go through the UI thread, as in the app: the device listing of the pool never runs while a test edits a page
        var viewModel = MainViewModelSetup.Create(dir, action => Dispatcher.UIThread.Post(action), dialogs: dialogs);
        if (connected) MainViewModelSetup.ShowServer(viewModel, FakeServers.Admin(sent));
        return viewModel;
    }

    static void Rename(AdminViewModel admin, string name) => admin.GroupList.SelectedGroup!.Name = name;

    [AvaloniaFact]
    public void HasChanges_UntouchedEditedOrReverted_OnlyTrueWhenEdited()
    {
        var settings = new SettingsViewModel(new ClientSettings(), null, null, TimeProvider.System);
        Assert.False(settings.HasChanges);
        var before = settings.VadThresholdDb;
        settings.VadThresholdDb = before - 5;
        Assert.True(settings.HasChanges);
        settings.VadThresholdDb = before;
        Assert.False(settings.HasChanges);
    }

    [AvaloniaFact]
    public async Task Admin_HasChanges_GroupDraftServerSettingsLinks()
    {
        var viewModel = Main();
        await viewModel.PageFlow.OpenAdminAsync();
        var admin = viewModel.PageFlow.AdminPage!;
        Assert.False(admin.HasChanges);

        admin.GroupList.SelectedGroup = admin.GroupList.Groups.First(groupEdit => groupEdit.IsEditable);
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
        var viewModel = Main();
        viewModel.PageFlow.OpenSettings();
        viewModel.PageFlow.SettingsPage!.VadThresholdDb -= 5;

        answers.Enqueue(LeaveChoice.Stay);
        await viewModel.PageFlow.OpenAdminCommand.ExecuteAsync(null); // a page change
        Assert.Equal(1, asked);
        Assert.True(viewModel.PageFlow.IsSettingsPage);
        Assert.True(viewModel.PageFlow.SettingsPage!.HasChanges); // the edit is untouched

        answers.Enqueue(LeaveChoice.Stay);
        await viewModel.PageFlow.ClosePageAsync(); // Esc, the X
        Assert.True(viewModel.PageFlow.IsSettingsPage);

        // disconnecting keeps the settings page: nothing of it is lost, nothing is asked
        await viewModel.Connection.ConfirmDisconnectCommand.ExecuteAsync(null);
        Assert.Equal(2, asked);
        Assert.Null(viewModel.Server);
    }

    [AvaloniaFact]
    public void SettingsCloseButton_UnsavedChanges_AsksFirst()
    {
        var mainViewModel = Main();
        mainViewModel.PageFlow.OpenSettings();
        var settings = mainViewModel.PageFlow.SettingsPage!;
        settings.VadThresholdDb -= 5;

        answers.Enqueue(LeaveChoice.Stay);
        settings.LeaveCommand.Execute(null);
        Assert.Equal(1, asked);
        Assert.True(mainViewModel.PageFlow.IsSettingsPage);

        answers.Enqueue(LeaveChoice.Discard);
        settings.LeaveCommand.Execute(null);
        Assert.Equal(2, asked);
        Assert.True(mainViewModel.PageFlow.IsHomePage);
    }

    [AvaloniaFact]
    public async Task Admin_Disconnect_AsksAndStays()
    {
        var viewModel = Main();
        await viewModel.PageFlow.OpenAdminAsync();
        viewModel.PageFlow.AdminPage!.ServerSettings.Name = "Neu";
        answers.Enqueue(LeaveChoice.Stay);
        await viewModel.Connection.ConfirmDisconnectCommand.ExecuteAsync(null);
        Assert.Equal(1, asked);
        Assert.NotNull(viewModel.Server);
        Assert.True(viewModel.PageFlow.IsAdminPage);
    }

    [AvaloniaFact]
    public async Task ConnectAsync_AdminHasChangesAndUserStays_AsksOnceAndKeepsTheShownServer()
    {
        var mainViewModel = Main();
        var shown = mainViewModel.Server;
        await mainViewModel.PageFlow.OpenAdminAsync();
        mainViewModel.PageFlow.AdminPage!.ServerSettings.Name = "Neu";
        answers.Enqueue(LeaveChoice.Stay);

        await mainViewModel.Connection.ConnectAsync(new ConnectChoice("127.0.0.1", 1, "anna", null, SaveBookmark: false));

        Assert.Equal(1, asked);
        Assert.Same(shown, mainViewModel.Server);
    }

    [AvaloniaFact]
    public async Task LeaveGuard_LeaveWithoutSaving_Discards()
    {
        var viewModel = Main();
        var saved = viewModel.Settings.VadThresholdDb;
        viewModel.PageFlow.OpenSettings();
        viewModel.PageFlow.SettingsPage!.VadThresholdDb = saved - 7;
        answers.Enqueue(LeaveChoice.Discard);
        await viewModel.PageFlow.OpenAdminCommand.ExecuteAsync(null);
        Assert.Equal(1, asked);
        Assert.True(viewModel.PageFlow.IsAdminPage);
        Assert.Equal(saved, viewModel.Settings.VadThresholdDb);
    }

    [AvaloniaFact]
    public async Task SaveAndLeave_LeavesAfterConfirm_StaysOnRefusal()
    {
        // settings: saved, then the new page
        var viewModel = Main();
        var saved = viewModel.Settings.VadThresholdDb;
        viewModel.PageFlow.OpenSettings();
        viewModel.PageFlow.SettingsPage!.VadThresholdDb = saved - 7;
        answers.Enqueue(LeaveChoice.Save);
        await viewModel.PageFlow.OpenAdminCommand.ExecuteAsync(null);
        Assert.True(viewModel.PageFlow.IsAdminPage);
        Assert.Equal(saved - 7, viewModel.Settings.VadThresholdDb);

        // a group the server refuses: the page stays with its error
        var admin = viewModel.PageFlow.AdminPage!;
        admin.GroupList.SelectedGroup = admin.GroupList.Groups.First(groupEdit => groupEdit.IsEditable);
        Rename(admin, "Umbenannt");
        answers.Enqueue(LeaveChoice.Save);
        var leaving = viewModel.PageFlow.ClosePageAsync();
        var update = sent.OfType<UpdateGroup>().Last();
        viewModel.Server!.Apply(new Error(update.RequestId, ReasonCodes.PermissionDenied));
        await leaving;
        Assert.True(viewModel.PageFlow.IsAdminPage);
        Assert.True(admin.GroupList.Place.HasChanges());

        // confirmed by the server's groups: the page goes
        answers.Enqueue(LeaveChoice.Save);
        leaving = viewModel.PageFlow.ClosePageAsync();
        update = sent.OfType<UpdateGroup>().Last();
        var groups = viewModel.Server.Mirror.Groups.Select(groupInfo => groupInfo.Id == update.GroupId ? groupInfo with { Name = update.Name } : groupInfo).ToList();
        viewModel.Server.Apply(new GroupsChanged(groups));
        await leaving;
        Assert.True(viewModel.PageFlow.IsHomePage);
    }

    [AvaloniaFact]
    public async Task LeaveGuard_CancelOrDiscardButton_DoesNotAsk()
    {
        var viewModel = Main();
        viewModel.PageFlow.OpenSettings();
        viewModel.PageFlow.SettingsPage!.VadThresholdDb -= 5;
        viewModel.PageFlow.SettingsPage.CancelCommand.Execute(null); // "Abbrechen"
        Assert.True(viewModel.PageFlow.IsHomePage);

        await viewModel.PageFlow.OpenAdminAsync();
        var links = viewModel.PageFlow.AdminPage!.Links;
        links.UnlinkAll();
        links.DiscardCommand.Execute(null); // "Verwerfen"
        Assert.False(viewModel.PageFlow.AdminPage.HasChanges);
        Assert.Equal(0, asked);
    }

    [AvaloniaFact]
    public async Task WindowClose_WithChanges_AsksAndStaysOpen()
    {
        var viewModel = Main();
        var main = new MainWindow { DataContext = viewModel, Width = 1100, Height = 700 };
        main.Show();
        await viewModel.PageFlow.OpenAdminAsync();
        viewModel.PageFlow.AdminPage!.ServerSettings.Name = "Neu";
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
        var viewModel = Main();
        var main = new MainWindow { DataContext = viewModel, Width = 1100, Height = 700 };
        main.Show();
        await viewModel.PageFlow.OpenAdminAsync();
        Dispatcher.UIThread.RunJobs();
        var page = Page(main);
        var tabs = page.FindControl<TabControl>("Tabs")!;
        var serverTab = page.FindControl<TabItem>("OverviewTab")!; // Package 154
        tabs.SelectedItem = serverTab;
        Dispatcher.UIThread.RunJobs();
        viewModel.PageFlow.AdminPage!.ServerSettings.Name = "Neu";

        answers.Enqueue(LeaveChoice.Stay);
        tabs.SelectedItem = page.FindControl<TabItem>("GroupsTab");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, asked);
        Assert.Same(serverTab, tabs.SelectedItem);
        Assert.Equal("Neu", viewModel.PageFlow.AdminPage.ServerSettings.Name);

        answers.Enqueue(LeaveChoice.Discard);
        tabs.SelectedItem = page.FindControl<TabItem>("GroupsTab");
        Dispatcher.UIThread.RunJobs();
        Assert.Same(page.FindControl<TabItem>("GroupsTab"), tabs.SelectedItem);
        Assert.Equal("Gilde", viewModel.PageFlow.AdminPage.ServerSettings.Name); // discarded
        main.Close();
    }

    [AvaloniaFact]
    public async Task OtherGroup_WithChanges_AsksAndStays()
    {
        var viewModel = Main();
        var main = new MainWindow { DataContext = viewModel, Width = 1100, Height = 700 };
        main.Show();
        await viewModel.PageFlow.OpenAdminAsync();
        Dispatcher.UIThread.RunJobs();
        AdminPages.Open(main, OVS.Client.Localization.Strings.Nav_GroupsRights); // Package 154: Übersicht comes first
        var list = Page(main).FindControl<ListBox>("GroupRows")!;
        var admin = viewModel.PageFlow.AdminPage!;
        var edited = admin.GroupList.Groups.First(groupEdit => groupEdit.IsEditable);
        list.SelectedItem = edited;
        Dispatcher.UIThread.RunJobs();
        Rename(admin, "Umbenannt");

        answers.Enqueue(LeaveChoice.Stay);
        list.SelectedItem = admin.GroupList.Groups.First(groupEdit => groupEdit != edited);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, asked);
        Assert.Same(edited, admin.GroupList.SelectedGroup);
        Assert.Equal("Umbenannt", edited.Name);

        answers.Enqueue(LeaveChoice.Discard);
        var other = admin.GroupList.Groups.First(groupEdit => groupEdit != edited);
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
        var viewModel = Main();
        var main = new MainWindow { DataContext = viewModel, Width = 1100, Height = 700 };
        main.Show();
        await viewModel.PageFlow.OpenAdminAsync();
        Dispatcher.UIThread.RunJobs();
        AdminPages.Open(main, OVS.Client.Localization.Strings.Nav_GroupsRights); // Package 154: Übersicht comes first
        var list = Page(main).FindControl<ListBox>("GroupRows")!;
        var admin = viewModel.PageFlow.AdminPage!;
        var edited = admin.GroupList.Groups.First(groupEdit => groupEdit.IsEditable);
        list.SelectedItem = edited;
        Dispatcher.UIThread.RunJobs();
        Rename(admin, "Umbenannt");

        answers.Enqueue(LeaveChoice.Save);
        var other = admin.GroupList.Groups.First(groupEdit => groupEdit != edited);
        list.SelectedItem = other;
        Dispatcher.UIThread.RunJobs();
        var update = sent.OfType<UpdateGroup>().Single();
        viewModel.Server!.Apply(new GroupsChanged(viewModel.Server.Mirror.Groups.Select(groupInfo => groupInfo.Id == update.GroupId ? groupInfo with { Name = update.Name } : groupInfo).ToList()));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(other.Id, admin.GroupList.SelectedGroup!.Id);
        Assert.False(admin.HasChanges);
        main.Close();
    }

    /// <summary>The page hands its leave question to the groups tab: a new group waits for the answer for the edited group.</summary>
    [AvaloniaFact]
    public async Task NewGroup_GroupEditedAndUserStays_AsksOnceAddsNoDraftAndKeepsTheEdit()
    {
        var viewModel = Main();
        await viewModel.PageFlow.OpenAdminAsync();
        var admin = viewModel.PageFlow.AdminPage!;
        admin.GroupList.SelectedGroup = admin.GroupList.Groups.First(groupEdit => groupEdit.IsEditable);
        Rename(admin, "Umbenannt");
        answers.Enqueue(LeaveChoice.Stay);

        await HangGuard.BoundAsync(admin.GroupList.NewGroupCommand.ExecuteAsync(null));

        Assert.Equal(1, asked);
        Assert.DoesNotContain(admin.GroupList.Groups, groupEdit => groupEdit.Id is null);
        Assert.Equal("Umbenannt", admin.GroupList.SelectedGroup!.Name);
    }

    /// <summary>Found in review: the server keeps line breaks as LF and a trimmed name; a saved edit counts as saved, and untouched fields follow another admin.</summary>
    [AvaloniaFact]
    public async Task ServerTab_SavedEditIsNoChange_UntouchedFollowsServer()
    {
        var viewModel = Main();
        await viewModel.PageFlow.OpenAdminAsync();
        var admin = viewModel.PageFlow.AdminPage!;
        var settings = viewModel.Server!.Mirror.Settings;
        viewModel.Server.Apply(new ServerSettingsChanged(settings with { Name = "Anderer Name" })); // another admin
        Assert.Equal("Anderer Name", admin.ServerSettings.Name);
        Assert.False(admin.HasChanges);

        admin.ServerSettings.Name = "Neu ";
        admin.ServerSettings.WelcomeText = "Zeile 1" + (char)13 + (char)10 + "Zeile 2";
        Assert.True(admin.ServerSettings.Place.HasChanges());
        viewModel.Server.Apply(new ServerSettingsChanged(viewModel.Server.Mirror.Settings with { Name = "Neu", WelcomeText = "Zeile 1" + (char)10 + "Zeile 2" }));
        Assert.False(admin.ServerSettings.Place.HasChanges());
    }

    /// <summary>Found in review: sliders turn floats into percent and back, which is not exact; opening is no change.</summary>
    [AvaloniaFact]
    public void Settings_SliderRoundTrip_IsNoChange()
    {
        var random = new Random(7);
        for (int index = 0; index < 200; index++)
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
    public void Settings_DeviceListArrivesWithViewBound_NoCrashNoChange()
    {
        new ClientSettings { InputDeviceId = "weg", OutputDeviceId = "b" }.Save(dir);
        var viewModel = Main(connected: false);
        var main = new MainWindow { DataContext = viewModel, Width = 1100, Height = 700 };
        main.Show();
        viewModel.PageFlow.OpenSettings();
        Dispatcher.UIThread.RunJobs();
        viewModel.PageFlow.SettingsPage!.ShowDevices([new OVS.Client.Audio.Devices.AudioDevice("a", "Mikrofon")], [new OVS.Client.Audio.Devices.AudioDevice("b", "Lautsprecher")]);
        Dispatcher.UIThread.RunJobs();
        Assert.False(viewModel.PageFlow.SettingsPage.HasChanges);
        main.Close();
    }

    /// <summary>Found in the second review: new settings from the server kept a typed password, and follow field by field.</summary>
    [AvaloniaFact]
    public async Task ServerTab_ArrivingSettings_KeepPasswordAndFollowPerField()
    {
        var viewModel = Main();
        await viewModel.PageFlow.OpenAdminAsync();
        var admin = viewModel.PageFlow.AdminPage!;
        admin.ServerSettings.NewPassword = "geheim";
        admin.ServerSettings.Name = "Mein Name";
        viewModel.Server!.Apply(new ServerSettingsChanged(viewModel.Server.Mirror.Settings with { WelcomeText = "Neu von jemand anderem" }));
        Assert.Equal("geheim", admin.ServerSettings.NewPassword);
        Assert.Equal("Mein Name", admin.ServerSettings.Name); // the edit stays
        Assert.Equal("Neu von jemand anderem", admin.ServerSettings.WelcomeText); // the untouched field follows
        Assert.True(admin.ServerSettings.Place.HasChanges());
    }

    /// <summary>Package 153: a way out with changes points at the bar first, then asks; without changes neither.</summary>
    [AvaloniaFact]
    public async Task Leave_WithChanges_AttentionThenQuestion()
    {
        var viewModel = Main(connected: false);
        answers.Enqueue(LeaveChoice.Stay);
        viewModel.PageFlow.OpenSettings();
        var settings = viewModel.PageFlow.SettingsPage!;
        settings.AttentionRequested += () => order.Add("attention");

        await viewModel.PageFlow.ClosePageAsync();
        Assert.Empty(order);
        Assert.True(viewModel.PageFlow.IsHomePage);

        viewModel.PageFlow.OpenSettings();
        settings = viewModel.PageFlow.SettingsPage!;
        settings.AttentionRequested += () => order.Add("attention");
        settings.VadThresholdDb -= 5;
        await viewModel.PageFlow.ClosePageAsync();
        Assert.Equal(["attention", "question"], order);
        Assert.True(viewModel.PageFlow.IsSettingsPage);
    }

    readonly List<string> order = [];
    /// <summary>Package 154: Übersicht and Betrieb share the server settings' place: between them no question, away from them one.</summary>
    [AvaloniaFact]
    public async Task Admin_OverviewChanged_LeavingAsks()
    {
        var viewModel = Main();
        var main = new MainWindow { DataContext = viewModel, Width = 1100, Height = 700 };
        main.Show();
        await viewModel.PageFlow.OpenAdminAsync();
        Dispatcher.UIThread.RunJobs();
        var page = Page(main);
        var tabs = page.FindControl<TabControl>("Tabs")!;
        viewModel.PageFlow.AdminPage!.ServerSettings.Name = "Neu";
        tabs.SelectedItem = page.FindControl<TabItem>("OperationTab");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, asked);
        answers.Enqueue(LeaveChoice.Stay);
        tabs.SelectedItem = page.FindControl<TabItem>("GroupsTab");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, asked);
        Assert.Same(page.FindControl<TabItem>("OperationTab"), tabs.SelectedItem);
        main.Close();
    }

    /// <summary>Package 154: a change on Betrieb asks too when another page is chosen.</summary>
    [AvaloniaFact]
    public async Task Admin_OperationChanged_LeavingAsks()
    {
        var viewModel = Main();
        var main = new MainWindow { DataContext = viewModel, Width = 1100, Height = 700 };
        main.Show();
        viewModel.Server!.Apply(new ServerSettingsChanged(viewModel.Server.Mirror.Settings with { Limits = new ServerLimits(50, 30, true, false, new TimeOnly(4, 0)) })); // with limits
        await viewModel.PageFlow.OpenAdminAsync();
        Dispatcher.UIThread.RunJobs();
        var page = Page(main);
        var tabs = page.FindControl<TabControl>("Tabs")!;
        tabs.SelectedItem = page.FindControl<TabItem>("OperationTab");
        Dispatcher.UIThread.RunJobs();
        viewModel.PageFlow.AdminPage!.ServerSettings.LogDays = (viewModel.PageFlow.AdminPage.ServerSettings.LogDays ?? 0) + 1;
        answers.Enqueue(LeaveChoice.Stay);
        tabs.SelectedItem = page.FindControl<TabItem>("LinksTab");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, asked);
        Assert.Same(page.FindControl<TabItem>("OperationTab"), tabs.SelectedItem);
        main.Close();
    }

}
