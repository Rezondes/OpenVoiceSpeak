using OVS.Client.Net;
using OVS.Client.Services;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.ServerInfo;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Services;

/// <summary>
/// Packages 30 and 122 and the retry after a refusal (fix commit d7801a9, no package): keeping a server's logo in step with its
/// hash (shown, from the cache, asked for, retried after a refusal, cached on arrival), without a window, a connection or a
/// network. The cache is a real one on a temp folder.
/// </summary>
public sealed class ServerIconTrackerTests : IDisposable
{
    const string Host = "voice.example.org";
    const int Port = 7000;

    static readonly byte[] Logo = [1, 2, 3];
    static readonly byte[] OtherLogo = [4, 5, 6];
    static readonly string LogoHash = ServerIconFormat.ComputeHash(Logo);
    static readonly string OtherLogoHash = ServerIconFormat.ComputeHash(OtherLogo);

    readonly string directory = Directory.CreateTempSubdirectory("ovs-icon-tracker-").FullName;
    readonly ServerIconCache cache;
    readonly ManualTimeProvider clock = new();
    readonly DelayRecorder time;
    readonly PostQueue posts = new();
    readonly List<byte[]?> shownLogos = [];
    readonly List<Action<Message>> asks = [];
    readonly ServerIconTracker tracker;
    string? serverHash;
    byte[]? displayedLogo;
    bool isCurrent = true;
    int tilesRefreshed;

    public ServerIconTrackerTests()
    {
        cache = new ServerIconCache(directory);
        time = new DelayRecorder(clock);
        tracker = new ServerIconTracker(cache, Host, Port, time, () => serverHash, () => displayedLogo, logo => shownLogos.Add(logo), asks.Add,
            () => isCurrent, posts.Post, () => tilesRefreshed++);
    }

    public void Dispose() => Directory.Delete(directory, true);

    void Refuse() => asks[^1](new Error(null, ReasonCodes.RateLimited));

    /// <summary>The retry delay passes; its posted action runs (the UI thread's turn).</summary>
    Task RunRetryAsync() => posts.RunAsync(clock, ServerIconTracker.RetryDelay);

    static ServerIcon BuildIconMessage(byte[] png, string hash) => new(null, hash, Convert.ToBase64String(png));

    // ---- Sync: what to do for the server's hash ----

    [Fact]
    public void Sync_ServerHasNoLogo_RemovesTheCachedLogoShowsNoneAndAsksNothing()
    {
        cache.Save(Host, Port, Logo);
        serverHash = null;

        tracker.Sync();

        Assert.Null(cache.Load(Host, Port));
        Assert.Null(Assert.Single(shownLogos));
        Assert.Empty(asks);
    }

    [Fact]
    public void Sync_ShownLogoHasTheServersHash_AsksNothingAndChangesNothing()
    {
        serverHash = LogoHash;
        displayedLogo = Logo;

        tracker.Sync();

        Assert.Empty(asks);
        Assert.Empty(shownLogos);
    }

    [Fact]
    public void Sync_ShownLogoHasAnotherHash_AsksTheServerAndShowsNothing()
    {
        serverHash = LogoHash;
        displayedLogo = OtherLogo;

        tracker.Sync();

        Assert.Single(asks);
        Assert.Empty(shownLogos);
    }

    [Fact]
    public void Sync_CachedLogoHasTheServersHash_ShowsItWithoutAsking()
    {
        cache.Save(Host, Port, Logo);
        serverHash = LogoHash;

        tracker.Sync();

        Assert.Empty(asks);
        Assert.Equal(Logo, Assert.Single(shownLogos));
    }

    [Fact]
    public void Sync_CachedLogoHasAnotherHash_AsksTheServerAndShowsNothing()
    {
        cache.Save(Host, Port, OtherLogo);
        serverHash = LogoHash;

        tracker.Sync();

        Assert.Single(asks);
        Assert.Empty(shownLogos);
    }

    [Fact]
    public void Sync_NoLogoKnownYet_AsksTheServerOnce()
    {
        serverHash = LogoHash;

        tracker.Sync();

        Assert.Single(asks);
    }

    [Fact]
    public void Sync_HashGoneAndBackAgain_AsksNothingMoreForTheSameLogo()
    {
        serverHash = LogoHash;
        tracker.Sync();
        serverHash = null;
        tracker.Sync();
        serverHash = LogoHash;

        tracker.Sync();

        Assert.Single(asks);
    }

    // ---- Sync: when it checks (Package 122) ----

    [Fact]
    public void Sync_FirstCall_ChecksOnce()
    {
        serverHash = null;

        tracker.Sync();

        Assert.Equal(1, tracker.Checks);
    }

    [Fact]
    public void Sync_SameHashAsLastTime_ChecksNothingMore()
    {
        serverHash = LogoHash;
        tracker.Sync();

        tracker.Sync();

        Assert.Equal(1, tracker.Checks);
    }

    [Fact]
    public void Sync_HashChanged_ChecksAgain()
    {
        serverHash = LogoHash;
        tracker.Sync();
        serverHash = OtherLogoHash;

        tracker.Sync();

        Assert.Equal(2, tracker.Checks);
    }

    // ---- the answer to the request: a refusal is retried once ----

    [Fact]
    public void Refusal_RateLimited_WaitsElevenSecondsAndAsksNothingYet()
    {
        serverHash = LogoHash;
        tracker.Sync();

        Refuse();

        Assert.Equal([TimeSpan.FromSeconds(11)], time.Delays);
        Assert.Single(asks);
    }

    [Fact]
    public void Answer_OtherError_SchedulesNoRetry()
    {
        serverHash = LogoHash;
        tracker.Sync();

        asks[^1](new Error(null, ReasonCodes.ServerFull));

        Assert.Empty(time.Delays);
    }

    [Fact]
    public void Answer_NotAnError_SchedulesNoRetry()
    {
        serverHash = LogoHash;
        tracker.Sync();

        asks[^1](new ServerIcon(null, LogoHash, null));

        Assert.Empty(time.Delays);
    }

    [Fact]
    public void Refusal_TwiceForTheSameLogo_SchedulesNoSecondRetry()
    {
        serverHash = LogoHash;
        tracker.Sync();
        Refuse();

        Refuse();

        Assert.Single(time.Delays);
    }

    [Fact]
    public void Refusal_ForALaterLogo_SchedulesAnotherRetry()
    {
        serverHash = LogoHash;
        tracker.Sync();
        Refuse();
        serverHash = OtherLogoHash;
        tracker.Sync();

        Refuse();

        Assert.Equal(2, time.Delays.Count);
    }

    [Fact]
    public async Task Retry_LogoStillWantedOnTheCurrentServer_AsksTheServerAgain()
    {
        serverHash = LogoHash;
        tracker.Sync();
        Refuse();
        var asksBefore = asks.Count;

        await RunRetryAsync();

        Assert.Equal(asksBefore + 1, asks.Count);
    }

    [Fact]
    public async Task Retry_RefusedAgain_SchedulesNoSecondRetry()
    {
        serverHash = LogoHash;
        tracker.Sync();
        Refuse();
        await RunRetryAsync();

        Refuse();

        Assert.Single(time.Delays);
    }

    [Fact]
    public async Task Retry_ServerNoLongerShown_AsksNothingMore()
    {
        serverHash = LogoHash;
        tracker.Sync();
        Refuse();
        isCurrent = false;

        await RunRetryAsync();

        Assert.Single(asks);
    }

    [Fact]
    public async Task Retry_ServersLogoChangedMeanwhile_AsksNothingMoreForTheOldOne()
    {
        serverHash = LogoHash;
        tracker.Sync();
        Refuse();
        serverHash = OtherLogoHash;

        await RunRetryAsync();

        Assert.Single(asks);
    }

    // ---- Receive: a logo arrives ----

    [Fact]
    public void Receive_LogoOfTheServersHash_ShowsItCachesItAndRefreshesTheTiles()
    {
        serverHash = LogoHash;

        tracker.Receive(BuildIconMessage(Logo, LogoHash));

        Assert.Equal(Logo, Assert.Single(shownLogos));
        Assert.Equal(Logo, cache.Load(Host, Port));
        Assert.Equal(1, tilesRefreshed);
    }

    [Fact]
    public void Receive_AnswerWithoutAPicture_ChangesNothing()
    {
        serverHash = LogoHash;

        tracker.Receive(new ServerIcon(null, LogoHash, null));

        Assert.Empty(shownLogos);
        Assert.Null(cache.Load(Host, Port));
        Assert.Equal(0, tilesRefreshed);
    }

    [Fact]
    public void Receive_LogoOfAnotherHashThanTheServers_ChangesNothing()
    {
        serverHash = LogoHash;

        tracker.Receive(BuildIconMessage(OtherLogo, OtherLogoHash));

        Assert.Empty(shownLogos);
        Assert.Null(cache.Load(Host, Port));
        Assert.Equal(0, tilesRefreshed);
    }

    [Fact]
    public void Receive_PictureThatDoesNotMatchItsHash_ChangesNothing()
    {
        serverHash = LogoHash;

        tracker.Receive(BuildIconMessage(OtherLogo, LogoHash));

        Assert.Empty(shownLogos);
        Assert.Null(cache.Load(Host, Port));
        Assert.Equal(0, tilesRefreshed);
    }
}
