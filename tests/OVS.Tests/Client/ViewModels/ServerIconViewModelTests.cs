using OVS.Client.ViewModels;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.ServerInfo;
using OVS.Tests.TestSupport;
using static OVS.Tests.TestSupport.HangGuard;

namespace OVS.Tests.Client.ViewModels;

/// <summary>The server icon of the settings card on its own: what it sends, how it waits for the server, and when it follows or lets go of it.</summary>
public sealed class ServerIconViewModelTests
{
    static readonly byte[] Png = [1, 2, 3];
    const string Refusal = "Das Bild ist kein PNG.";

    readonly ManualTimeProvider time = new();
    readonly List<Request> sent = [];

    sealed record Rig(ServerIconViewModel Section, ServerViewModel Server);

    Rig Create()
    {
        var server = FakeServers.Admin(sent, time);
        return new Rig(new ServerIconViewModel(server), server);
    }

    static List<string?> RaisedBy(ServerIconViewModel section)
    {
        var raised = new List<string?>();
        section.PropertyChanged += (_, args) => raised.Add(args.PropertyName);
        return raised;
    }

    static void ServerCarriesAnIcon(ServerViewModel server) =>
        server.Apply(new ServerSettingsChanged(server.Mirror.Settings with { IconHash = "abc" }));

    [Fact]
    public async Task UploadIconAsync_PngGiven_SendsItAsBase64AndWaitsForTheSettings()
    {
        var rig = Create();

        await Bound(rig.Section.UploadIconAsync(Png, null));

        Assert.Equal(Convert.ToBase64String(Png), sent.OfType<SetServerIcon>().Single().PngBase64);
        Assert.True(rig.Section.IconUpload.IsRunning);
    }

    [Fact]
    public async Task UploadIconAsync_RefusalOnly_ShowsTheRefusalAndSendsNothing()
    {
        var rig = Create();

        await Bound(rig.Section.UploadIconAsync(null, Refusal));

        Assert.Equal(Refusal, rig.Section.IconError);
        Assert.Empty(sent);
        Assert.False(rig.Section.IconUpload.IsRunning);
    }

    [Fact]
    public async Task UploadIconAsync_RefusalShownBefore_RefusalGoneAndPngSent()
    {
        var rig = Create();
        await Bound(rig.Section.UploadIconAsync(null, Refusal));

        await Bound(rig.Section.UploadIconAsync(Png, null));

        Assert.Null(rig.Section.IconError);
        Assert.Single(sent.OfType<SetServerIcon>());
    }

    [Fact]
    public async Task UploadIconAsync_AnotherUploadStillWaiting_SendsNoSecondOne()
    {
        var rig = Create();
        await Bound(rig.Section.UploadIconAsync(Png, null));

        await Bound(rig.Section.UploadIconAsync([4, 5], null));

        Assert.Single(sent.OfType<SetServerIcon>());
    }

    [Fact]
    public async Task UploadIconAsync_SettingsConfirmTheFirst_TheNextOneIsSent()
    {
        var rig = Create();
        await Bound(rig.Section.UploadIconAsync(Png, null));
        ServerCarriesAnIcon(rig.Server);

        await Bound(rig.Section.UploadIconAsync([4, 5], null));

        Assert.Equal([Convert.ToBase64String(Png), Convert.ToBase64String([4, 5])], sent.OfType<SetServerIcon>().Select(request => request.PngBase64));
    }

    [Fact]
    public async Task UploadIconAsync_NoAnswerWithinTheTimeout_StopsWaitingShowsTheErrorAndSendsNoNotice()
    {
        var rig = Create();
        var notices = new List<string>();
        rig.Server.Notice += notices.Add;
        await Bound(rig.Section.UploadIconAsync(Png, null));

        time.Advance(PendingViewModel.Timeout + TimeSpan.FromSeconds(1));

        Assert.True(rig.Section.IconUpload.HasError);
        Assert.False(rig.Section.IconUpload.IsRunning);
        Assert.Empty(notices);
    }

    [Fact]
    public async Task RemoveIcon_ReasonShown_SendsAnEmptyIconClearsTheReasonAndWaits()
    {
        var rig = Create();
        await Bound(rig.Section.UploadIconAsync(null, Refusal));

        rig.Section.RemoveIconCommand.Execute(null);

        Assert.Null(rig.Section.IconError);
        Assert.Null(sent.OfType<SetServerIcon>().Single().PngBase64);
        Assert.True(rig.Section.IconUpload.IsRunning);
    }

    [Fact]
    public async Task RemoveIcon_UploadWaitingAndReasonShown_ClearsTheReasonAndSendsNothingMore()
    {
        var rig = Create();
        await Bound(rig.Section.UploadIconAsync(Png, null));
        await Bound(rig.Section.UploadIconAsync(null, Refusal));

        rig.Section.RemoveIconCommand.Execute(null);

        Assert.Null(rig.Section.IconError);
        Assert.Single(sent.OfType<SetServerIcon>());
    }

    [Fact]
    public void HasIcon_ServerHasNoIcon_False()
    {
        var rig = Create();

        Assert.False(rig.Section.HasIcon);
    }

    [Fact]
    public void FollowServer_ServerCarriesAnIconNow_HasIconTrueAndRaised()
    {
        var rig = Create();
        var raised = RaisedBy(rig.Section);
        ServerCarriesAnIcon(rig.Server);

        rig.Section.FollowServer();

        Assert.True(rig.Section.HasIcon);
        Assert.Equal([nameof(ServerIconViewModel.HasIcon)], raised);
    }

    [Fact]
    public void IconPng_ServerGetsAnIcon_ReadsItAndRaisesIconPngAndHasIcon()
    {
        var rig = Create();
        var raised = RaisedBy(rig.Section);

        rig.Server.IconPng = Png;

        Assert.Same(Png, rig.Section.IconPng);
        Assert.Equal([nameof(ServerIconViewModel.IconPng), nameof(ServerIconViewModel.HasIcon)], raised);
    }

    [Fact]
    public void PropertyChanged_ServerRenamed_NothingRaised()
    {
        var rig = Create();
        var raised = RaisedBy(rig.Section);

        rig.Server.ServerName = "Neu";

        Assert.Empty(raised);
    }

    [Fact]
    public void Detach_ServerIconPngChangesAfterwards_NothingRaised()
    {
        var rig = Create();
        var raised = RaisedBy(rig.Section);
        rig.Section.Detach();

        rig.Server.IconPng = Png;

        Assert.Empty(raised);
    }
}
