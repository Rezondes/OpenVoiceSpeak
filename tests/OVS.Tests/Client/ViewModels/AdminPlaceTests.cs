using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OVS.Client.Models;
using OVS.Client.ViewModels;
using OVS.Client.Views;
using OVS.Shared.Protocol.Messages;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.ViewModels;

/// <summary>
/// Package 114: the places of the administration that need a manual save. Each tab carries its place, so a new
/// place is one tab and one entry of <see cref="AdminViewModel.Places"/>, no switch to edit.
/// </summary>
[Collection(MotionCollection.Name)]
public sealed class AdminPlaceTests
{
    // a clock that never moves on its own: no pending save times out while a test waits
    static AdminViewModel Admin(List<Request>? sent = null) => new(FakeServers.Admin(sent, new ManualTimeProvider()));

    /// <summary>Shows the page with the view model and hands its tabs over; the window closes afterwards.</summary>
    static void WithTabs(AdminViewModel admin, Action<AdminView, IReadOnlyList<TabItem>> check)
    {
        var page = new AdminView { DataContext = admin };
        var window = new Window { Content = page, Width = 1100, Height = 700 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            check(page, page.FindControl<TabControl>("Tabs")!.Items.OfType<TabItem>().ToList());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Places_EveryPlace_CarriedByItsPagesOnly()
    {
        var admin = Admin();
        WithTabs(admin, (_, tabs) =>
        {
            // Package 154: Übersicht and Betrieb share the server settings' place (one request saves both)
            foreach (var place in admin.Places)
                Assert.Equal(place == admin.ServerSettings.Place ? 2 : 1, tabs.Count(tabItem => ReferenceEquals(tabItem.Tag, place)));
            Assert.All(tabs.Where(tabItem => tabItem.Tag is AdminPlace), tabItem => Assert.Contains(tabItem.Tag as AdminPlace, admin.Places));
        });
    }

    [AvaloniaFact]
    public void SavingTabs_DataContextSet_CarryTheirOwnPlace()
    {
        var admin = Admin();
        WithTabs(admin, (page, _) =>
        {
            Assert.Same(admin.GroupList.Place, page.FindControl<TabItem>("GroupsTab")!.Tag);
            Assert.Same(admin.Links.Place, page.FindControl<TabItem>("LinksTab")!.Tag);
            Assert.Same(admin.ServerSettings.Place, page.FindControl<TabItem>("OverviewTab")!.Tag);
            Assert.Same(admin.ServerSettings.Place, page.FindControl<TabItem>("OperationTab")!.Tag);
        });
    }

    [AvaloniaFact]
    public void Places_SavingOrder_GroupsLinksServer()
    {
        var admin = Admin();
        Assert.Equal([admin.GroupList.Place, admin.Links.Place, admin.ServerSettings.Place], admin.Places);
    }

    [AvaloniaFact]
    public void HasChanges_OnlyLinksChangedThenDiscarded_TrueThenFalse()
    {
        var admin = Admin();
        admin.Links.UnlinkAll();
        Assert.False(admin.GroupList.Place.HasChanges());
        Assert.False(admin.ServerSettings.Place.HasChanges());
        Assert.True(admin.HasChanges);

        admin.Links.Place.Discard();

        Assert.False(admin.HasChanges);
    }

    [AvaloniaFact]
    public async Task SaveAsync_NoChanges_TrueWithoutSending()
    {
        var sent = new List<Request>();
        var admin = Admin(sent);
        foreach (var place in admin.Places) Assert.True(await HangGuard.BoundAsync(place.SaveAsync()));
        Assert.Empty(sent);
    }
    /// <summary>Package 157: the bar's "Zurücksetzen" puts each place back to what the server last sent.</summary>
    [AvaloniaFact]
    public async Task Reset_EachPlace_BackToServerState()
    {
        var admin = Admin();
        admin.ShownPlace = admin.ServerSettings.Place;
        var name = admin.ServerSettings.Name;
        admin.ServerSettings.Name = "Neu";
        Assert.True(admin.ShownPlaceHasChanges);
        admin.ResetShownPlaceCommand.Execute(null);
        Assert.Equal((name, false), (admin.ServerSettings.Name, admin.ShownPlaceHasChanges));

        admin.ShownPlace = admin.GroupList.Place;
        admin.GroupList.SelectedGroup = admin.GroupList.Groups.First(group => group.IsEditable);
        admin.GroupList.SelectedGroup.Name += "2";
        await admin.GroupList.NewGroupCommand.ExecuteAsync(null); // a draft as well
        Assert.True(admin.ShownPlaceHasChanges);
        admin.ResetShownPlaceCommand.Execute(null);
        Assert.False(admin.ShownPlaceHasChanges);

        admin.ShownPlace = admin.Links.Place;
        admin.Links.Set(FakeServers.Lobby, FakeServers.Raid, false);
        Assert.True(admin.ShownPlaceHasChanges);
        Assert.Equal(admin.Links.PendingText, admin.ShownPlaceNote);
        admin.ResetShownPlaceCommand.Execute(null);
        Assert.Equal((false, null), (admin.ShownPlaceHasChanges, admin.ShownPlaceNote));
    }

}
