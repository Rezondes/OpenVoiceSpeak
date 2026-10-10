using OVS.Client.Services;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.ServerInfo;
using OVS.Shared.Protocol.Messages.Users;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.ViewModels;

/// <summary>
/// Package 30: what <c>ConnectionViewModel.TrackServerIcon</c> hands the logo tracking (the server's hash and logo, the request and
/// its route, the current-server test, the posting, the server's clock, the bookmark tiles); the rules themselves are in
/// <c>ServerIconTrackerTests</c>. The facts here replace the real-server-free half of the former wall-clock test
/// <c>MainViewModelTests.ServerIcon_RateLimited_RetriedOnceOnlyForTheNewestRequest</c>.
/// </summary>
public sealed class ServerIconWiringTests : IAsyncLifetime
{
    const string Host = "127.0.0.1";
    const int Port = 1;

    static readonly byte[] Logo = [1, 2, 3];
    static readonly string LogoHash = ServerIconFormat.ComputeHash(Logo);

    readonly string directory = Directory.CreateTempSubdirectory("ovs-icon-wiring-").FullName;
    readonly ManualTimeProvider manualTime = new();
    readonly DelayRecorder serverTime;
    readonly PostQueue posts = new();
    readonly List<Request> sent = [];
    MainViewModel mainViewModel = null!;

    public ServerIconWiringTests() => serverTime = new DelayRecorder(manualTime);

    public Task InitializeAsync()
    {
        mainViewModel = MainViewModelSetup.Create(directory, posts.Post, time: manualTime);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await mainViewModel.DisposeAsync();
        Directory.Delete(directory, true);
    }

    ServerViewModel CreateServerWithLogoHash(string? hash)
    {
        var snapshot = new ServerSnapshot(new ServerSettingsInfo("Gilde", "", false, IconHash: hash), FakeServers.Lobby,
            [new ChannelInfo(FakeServers.Lobby, "Lobby", "", 0)], [], [],
            [new UserInfo(1, "fp1", "ich", FakeServers.Lobby, false, false, false, OVS.Shared.Permissions.Permission.None, [])]);
        return new ServerViewModel(Snapshots.Mirror(snapshot), request =>
        {
            lock (sent) sent.Add(request);
            return Task.CompletedTask;
        }, serverTime);
    }

    List<Request> IconRequests()
    {
        lock (sent) return sent.Where(request => request is GetServerIcon).ToList();
    }

    int CountIconRequests() => IconRequests().Count;

    string LastIconRequestId() => IconRequests().Last().RequestId!;

    ServerViewModel ShowTrackedServer(string? hash)
    {
        var server = CreateServerWithLogoHash(hash);
        MainViewModelSetup.ShowServer(mainViewModel, server);
        mainViewModel.Connection.TrackServerIcon(server, Host, Port);
        return server;
    }

    /// <summary>The server refuses the newest logo request for asking too often.</summary>
    static void Refuse(ServerViewModel server, string requestId) => server.Apply(new Error(requestId, ReasonCodes.RateLimited));

    [Fact]
    public void Apply_LogoOfTheServersHash_ShowsItOnTheServerAndOnTheBookmarkTile()
    {
        mainViewModel.Settings.Bookmarks.Add(new Bookmark("Gilde", Host, Port, "anna"));
        mainViewModel.Bookmarks.Refresh();
        var server = ShowTrackedServer(LogoHash);

        server.Apply(new ServerIcon(null, LogoHash, Convert.ToBase64String(Logo)));

        Assert.Equal(Logo, server.IconPng);
        Assert.Equal(Logo, mainViewModel.Bookmarks.Items.Single().Icon);
    }

    [Fact]
    public void TrackServerIcon_ServerAlreadyShowsTheLogo_AsksNothing()
    {
        var server = CreateServerWithLogoHash(LogoHash);
        server.IconPng = Logo;
        MainViewModelSetup.ShowServer(mainViewModel, server);

        mainViewModel.Connection.TrackServerIcon(server, Host, Port);

        Assert.Equal(0, CountIconRequests());
    }

    [Fact]
    public void TrackServerIcon_LogoNotShownYet_SendsOneIconRequest()
    {
        ShowTrackedServer(LogoHash);

        Assert.Equal(1, CountIconRequests());
    }

    [Fact]
    public void Apply_HashChanged_AsksForTheNewLogoAndDropsTheOlderRequestsRoute()
    {
        var server = ShowTrackedServer(LogoHash);
        var olderRequestId = LastIconRequestId();

        server.Apply(new ServerSettingsChanged(server.Mirror.Settings with { IconHash = "def" }));

        Assert.Equal(2, CountIconRequests());
        Assert.Null(server.Routes.Find(olderRequestId));
        Assert.NotNull(server.Routes.Find(LastIconRequestId()));
    }

    [Fact]
    public async Task Retry_ServerStillShown_AsksAgain()
    {
        var server = ShowTrackedServer(LogoHash);
        Refuse(server, LastIconRequestId());
        var requestsBefore = CountIconRequests();
        posts.Clear();

        await posts.RunAsync(manualTime, ServerIconTracker.RetryDelay);

        Assert.Equal(requestsBefore + 1, CountIconRequests());
    }

    [Fact]
    public async Task Retry_AnotherServerShownMeanwhile_AsksNothingMore()
    {
        var server = ShowTrackedServer(LogoHash);
        Refuse(server, LastIconRequestId());
        MainViewModelSetup.ShowServer(mainViewModel, CreateServerWithLogoHash(null));
        var requestsBefore = CountIconRequests();
        posts.Clear();

        await posts.RunAsync(manualTime, ServerIconTracker.RetryDelay);

        Assert.Equal(requestsBefore, CountIconRequests());
    }

    [Fact]
    public void Refusal_RateLimited_WaitsTheRetryDelayOnTheServersClock()
    {
        var server = ShowTrackedServer(LogoHash);

        Refuse(server, LastIconRequestId());

        Assert.Contains(ServerIconTracker.RetryDelay, serverTime.Delays);
    }
}
