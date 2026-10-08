using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OVS.Client.Models;
using OVS.Client.ViewModels;
using OVS.Client.Views;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Groups;
using OVS.Shared.Protocol.Messages.Links;
using OVS.Shared.Protocol.Messages.ServerInfo;
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
    public void Places_EveryPlace_CarriedByExactlyOneTab()
    {
        var admin = Admin();
        WithTabs(admin, (_, tabs) =>
        {
            foreach (var place in admin.Places) Assert.Single(tabs, t => ReferenceEquals(t.Tag, place));
            Assert.All(tabs.Where(t => t.Tag is AdminPlace), t => Assert.Contains(t.Tag as AdminPlace, admin.Places));
        });
    }

    [AvaloniaFact]
    public void SavingTabs_DataContextSet_CarryTheirOwnPlace()
    {
        var admin = Admin();
        WithTabs(admin, (page, _) =>
        {
            Assert.Same(admin.GroupsPlace, page.FindControl<TabItem>("GroupsTab")!.Tag);
            Assert.Same(admin.LinksPlace, page.FindControl<TabItem>("LinksTab")!.Tag);
            Assert.Same(admin.ServerPlace, page.FindControl<TabItem>("ServerTab")!.Tag);
        });
    }

    [AvaloniaFact]
    public void Places_SavingOrder_GroupsLinksServer()
    {
        var admin = Admin();
        Assert.Equal([admin.GroupsPlace, admin.LinksPlace, admin.ServerPlace], admin.Places);
    }

    [AvaloniaFact]
    public void HasAnyChanges_OnlyLinksChanged_True()
    {
        var admin = Admin();
        admin.Links.UnlinkAll();
        Assert.False(admin.GroupsPlace.HasChanges());
        Assert.False(admin.ServerPlace.HasChanges());
        Assert.True(admin.HasAnyChanges);
    }

    [AvaloniaFact]
    public async Task SaveAsync_NoChanges_TrueWithoutSending()
    {
        var sent = new List<Request>();
        var admin = Admin(sent);
        foreach (var place in admin.Places) Assert.True(await place.SaveAsync());
        Assert.Empty(sent);
    }

    [AvaloniaFact]
    public async Task SaveAsync_GroupSaveOnItsWay_WaitsForItAndSendsNoSecond()
    {
        var sent = new List<Request>();
        var server = FakeServers.Admin(sent, new ManualTimeProvider());
        var admin = new AdminViewModel(server);
        admin.SelectedGroup = admin.Groups.First(g => g.IsEditable);
        admin.SelectedGroup.Name = "Umbenannt";
        await admin.SaveGroupCommand.ExecuteAsync(null); // the save on its way
        var saving = admin.GroupsPlace.SaveAsync();
        Assert.False(saving.IsCompleted);

        var update = Assert.Single(sent.OfType<UpdateGroup>());
        server.Apply(new GroupsChanged(server.Mirror.Groups.Select(g => g.Id == update.GroupId ? g with { Name = update.Name } : g).ToList()));
        Assert.True(await saving);
        Assert.Single(sent.OfType<UpdateGroup>());
    }

    [AvaloniaFact]
    public async Task SaveAsync_LinkSaveOnItsWay_WaitsForItInsteadOfFailing()
    {
        var sent = new List<Request>();
        var server = FakeServers.Admin(sent, new ManualTimeProvider());
        var admin = new AdminViewModel(server);
        admin.Links.UnlinkAll();
        await admin.Links.ApplyCommand.ExecuteAsync(null); // the save on its way
        var saving = admin.LinksPlace.SaveAsync();
        Assert.False(saving.IsCompleted);

        server.Apply(new ChannelsUnlinked(FakeServers.Lobby, FakeServers.Raid));
        Assert.True(await saving);
        Assert.Single(sent.OfType<SetChannelLinks>());
    }

    [AvaloniaFact]
    public async Task SaveAsync_LinksChanged_SendsTheLinksAndTrueOnceConfirmed()
    {
        var sent = new List<Request>();
        var server = FakeServers.Admin(sent, new ManualTimeProvider());
        var admin = new AdminViewModel(server);
        admin.Links.UnlinkAll();
        var saving = admin.LinksPlace.SaveAsync();
        var request = Assert.Single(sent.OfType<SetChannelLinks>());
        Assert.Single(request.Remove);
        Assert.Empty(sent.OfType<UpdateServerSettings>());
        Assert.False(saving.IsCompleted);

        server.Apply(new ChannelsUnlinked(FakeServers.Lobby, FakeServers.Raid));
        Assert.True(await saving);
        Assert.False(admin.LinksPlace.HasChanges());
    }

    [AvaloniaFact]
    public async Task SaveAsync_ServerNameChanged_SendsTheSettingsAndTrueOnceConfirmed()
    {
        var sent = new List<Request>();
        var server = FakeServers.Admin(sent, new ManualTimeProvider());
        var admin = new AdminViewModel(server);
        admin.ServerName = "Neu";
        var saving = admin.ServerPlace.SaveAsync();
        Assert.Equal("Neu", Assert.Single(sent.OfType<UpdateServerSettings>()).Name);
        Assert.Empty(sent.OfType<SetChannelLinks>());
        Assert.False(saving.IsCompleted);

        server.Apply(new ServerSettingsChanged(server.Mirror.Settings with { Name = "Neu" }));
        Assert.True(await saving);
        Assert.False(admin.ServerPlace.HasChanges());
    }

    [AvaloniaFact]
    public async Task SaveAsync_ServerNameBlank_FalseWithoutSending()
    {
        var sent = new List<Request>();
        var admin = Admin(sent);
        admin.ServerName = "   ";
        Assert.True(admin.ServerPlace.HasChanges());
        Assert.False(await admin.ServerPlace.SaveAsync());
        Assert.Empty(sent);
    }

    [AvaloniaFact]
    public void Discard_LinksChanged_NoChangesLeft()
    {
        var admin = Admin();
        admin.Links.UnlinkAll();
        Assert.True(admin.LinksPlace.HasChanges());
        admin.LinksPlace.Discard();
        Assert.False(admin.LinksPlace.HasChanges());
        Assert.False(admin.HasAnyChanges);
    }
}
