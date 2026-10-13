using OVS.Client.ViewModels;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.ServerInfo;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.ViewModels;

/// <summary>The server icon of the settings card on its own: what it sends, how it waits for the server, and when it follows or lets go of it.</summary>
public sealed class ServerIconViewModelTests
{
    static readonly byte[] Png = [1, 2, 3];
    const string Refusal = "Das Bild ist kein PNG.";

    readonly ManualTimeProvider time = new();
    readonly List<Request> sent = [];

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
    public void UploadIcon_PngGiven_SendsItAsBase64AndWaitsForTheSettings()
    {
        var rig = Create();

        rig.Section.UploadIcon(Png, null);

        Assert.Equal(Convert.ToBase64String(Png), sent.OfType<SetServerIcon>().Single().PngBase64);
        Assert.True(rig.Section.IconUpload.IsRunning);
    }

    [Fact]
    public void UploadIcon_RefusalOnly_ShowsTheRefusalAndSendsNothing()
    {
        var rig = Create();

        rig.Section.UploadIcon(null, Refusal);

        Assert.Equal(Refusal, rig.Section.IconError);
        Assert.Empty(sent);
        Assert.False(rig.Section.IconUpload.IsRunning);
    }

    [Fact]
    public void UploadIcon_RefusalShownBefore_RefusalGoneAndPngSent()
    {
        var rig = Create();
        rig.Section.UploadIcon(null, Refusal);

        rig.Section.UploadIcon(Png, null);

        Assert.Null(rig.Section.IconError);
        Assert.Single(sent.OfType<SetServerIcon>());
    }

    [Fact]
    public void UploadIcon_AnotherUploadStillWaiting_SendsNoSecondOne()
    {
        var rig = Create();
        rig.Section.UploadIcon(Png, null);

        rig.Section.UploadIcon([4, 5], null);

        Assert.Single(sent.OfType<SetServerIcon>());
    }

    [Fact]
    public void UploadIcon_SettingsConfirmTheFirst_TheNextOneIsSent()
    {
        var rig = Create();
        rig.Section.UploadIcon(Png, null);
        ServerCarriesAnIcon(rig.Server);

        rig.Section.UploadIcon([4, 5], null);

        Assert.Equal([Convert.ToBase64String(Png), Convert.ToBase64String([4, 5])], sent.OfType<SetServerIcon>().Select(request => request.PngBase64));
    }

    [Fact]
    public void UploadIcon_NoAnswerWithinTheTimeout_StopsWaitingShowsTheErrorAndSendsNoNotice()
    {
        var rig = Create();
        var notices = new List<string>();
        rig.Server.Notice += notices.Add;
        rig.Section.UploadIcon(Png, null);

        time.Advance(PendingActionViewModel.Timeout + TimeSpan.FromSeconds(1));

        Assert.True(rig.Section.IconUpload.HasError);
        Assert.False(rig.Section.IconUpload.IsRunning);
        Assert.Empty(notices);
    }

    [Fact]
    public void RemoveIcon_ReasonShown_SendsAnEmptyIconClearsTheReasonAndWaits()
    {
        var rig = Create();
        rig.Section.UploadIcon(null, Refusal);

        rig.Section.RemoveIconCommand.Execute(null);

        Assert.Null(rig.Section.IconError);
        Assert.Null(sent.OfType<SetServerIcon>().Single().PngBase64);
        Assert.True(rig.Section.IconUpload.IsRunning);
    }

    [Fact]
    public void RemoveIcon_UploadWaitingAndReasonShown_ClearsTheReasonAndSendsNothingMore()
    {
        var rig = Create();
        rig.Section.UploadIcon(Png, null);
        rig.Section.UploadIcon(null, Refusal);

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
    public void FollowServer_ServerReportsAnIconHashBeforeItsPngIsDownloaded_HasIconTrueAndRaised()
    {
        var rig = Create();
        ServerCarriesAnIcon(rig.Server);
        Assert.Null(rig.Section.IconPng);
        var raised = RaisedBy(rig.Section);

        rig.Section.FollowServer();

        Assert.True(rig.Section.HasIcon);
        Assert.Equal([nameof(ServerIconViewModel.HasIcon)], raised);
    }

    [Fact]
    public void IconPng_ServerPngArrives_ReadsItAndRaisesOnlyIconPng()
    {
        var rig = Create();
        var raised = RaisedBy(rig.Section);

        rig.Server.IconPng = Png;

        Assert.Same(Png, rig.Section.IconPng);
        Assert.Equal([nameof(ServerIconViewModel.IconPng)], raised);
    }

    [Fact]
    public void UploadIcon_PngGiven_RegistersNoAnswerRoute()
    {
        var rig = Create();

        rig.Section.UploadIcon(Png, null);

        Assert.Null(rig.Server.Routes.Find(Assert.Single(sent).RequestId!));
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

    sealed record Rig(ServerIconViewModel Section, ServerViewModel Server);
}
