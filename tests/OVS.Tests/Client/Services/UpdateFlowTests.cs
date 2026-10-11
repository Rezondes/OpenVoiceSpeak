using OVS.Client.Localization;
using OVS.Client.Net.Updates;
using OVS.Client.Services;
using OVS.Shared;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Services;

/// <summary>Packages 43 and 62: the update check, the offer, the install and its progress card, without a window, a network or a disk.</summary>
public sealed class UpdateFlowTests
{
    static readonly UpdateOffer Offer = new("280926.0b2c", "deploy-bbbbbbb", "- new", DateTimeOffset.UnixEpoch,
        new Uri("https://example.org/OVS.Client.exe"), new Uri("https://example.org/OVS.Client.exe.sha256"));

    static readonly string AvailableLine = $"Update available: {Offer.Version} ({Offer.Tag})";

    readonly List<string> logLines = [];
    readonly List<string> statuses = [];
    readonly List<string> warnings = [];
    readonly List<UpdateProgress?> shownProgress = [];
    readonly List<string?> changedProperties = [];
    readonly UpdateFlow flow;
    bool checkAtStart;
    Func<UpdateOffer, Task<bool>>? offerDialog;
    int lookUps;
    int installs;
    CancellationToken lookUpToken;

    public UpdateFlowTests()
    {
        flow = Build(TimeSpan.FromSeconds(10));
        flow.PropertyChanged += (_, args) =>
        {
            changedProperties.Add(args.PropertyName);
            if (args.PropertyName == nameof(UpdateFlow.Progress)) shownProgress.Add(flow.Progress);
        };
    }

    UpdateFlow Build(TimeSpan lookUpTimeout) =>
        new(() => checkAtStart, offer => offerDialog?.Invoke(offer) ?? Task.FromResult(false), logLines.Add, statuses.Add, warnings.Add, lookUpTimeout);

    void LookUpReturns(UpdateCheckResult result, UpdateFlow? target = null) =>
        (target ?? flow).LookUp = token =>
        {
            lookUps++;
            lookUpToken = token;
            return Task.FromResult(result);
        };

    void InstallReturns(string? failure, params UpdateProgress[] reports) =>
        flow.Install = (_, report) =>
        {
            installs++;
            foreach (var progress in reports) report(progress);
            return Task.FromResult(failure);
        };

    static object Release(string tag) => new
    {
        tag_name = tag,
        name = "OpenVoiceSpeak 280926.0b2c",
        body = "",
        published_at = DateTimeOffset.UtcNow,
        assets = new[]
        {
            new { name = "OVS.Client.exe", browser_download_url = "https://example.org/OVS.Client.exe" },
            new { name = "OVS.Client.exe.sha256", browser_download_url = "https://example.org/OVS.Client.exe.sha256" },
        },
    };

    static UpdateChecker Checker(FakeHttp http, bool isCi) =>
        new(new HttpClient(http), new BuildInfo(DateTimeOffset.UtcNow.AddDays(-1), "aaaaaaa", IsCi: isCi));

    // ---- the wiring ----

    [Fact]
    public async Task Use_LocalBuildChecker_CheckAsyncSaysLocalBuildAndSendsNoRequest()
    {
        var http = new FakeHttp(_ => FakeHttp.Json(Release("deploy-bbbbbbb")));
        flow.Use(Checker(http, isCi: false), null);

        var answer = await HangGuard.Bound(flow.CheckAsync());

        Assert.Equal(Strings.Update_LocalBuild, answer);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task Use_ContinuousIntegrationChecker_CheckAsyncAsksGitHub()
    {
        var http = new FakeHttp(_ => FakeHttp.Json(Release("deploy-bbbbbbb")));
        flow.Use(Checker(http, isCi: true), null);

        var answer = await HangGuard.Bound(flow.CheckAsync());

        Assert.Single(http.Requests);
        Assert.Equal(string.Format(Strings.Update_Available, "280926.0b2c"), answer);
    }

    [Fact]
    public void Use_NoInstaller_InstallStaysEmpty()
    {
        flow.Use(Checker(new FakeHttp(_ => FakeHttp.Json(Release("deploy-bbbbbbb"))), isCi: true), null);

        Assert.Null(flow.Install);
    }

    // ---- the check ----

    [Fact]
    public async Task CheckAsync_NoLookUp_SaysLocalBuildAndLogsNothing()
    {
        var answer = await HangGuard.Bound(flow.CheckAsync());

        Assert.Equal(Strings.Update_LocalBuild, answer);
        Assert.Empty(logLines);
    }

    [Fact]
    public async Task CheckAsync_LookUpFails_SaysCheckFailedAndLogsTheReason()
    {
        LookUpReturns(new UpdateCheckResult(null, "no network"));

        var answer = await HangGuard.Bound(flow.CheckAsync());

        Assert.Equal(string.Format(Strings.Update_CheckFailed, "no network"), answer);
        Assert.Equal(["Update check failed: no network"], logLines);
    }

    [Fact]
    public async Task CheckAsync_NothingNewer_SaysLatestAndLogsIt()
    {
        LookUpReturns(new UpdateCheckResult(null));

        var answer = await HangGuard.Bound(flow.CheckAsync());

        Assert.Equal(Strings.Update_Latest, answer);
        Assert.Equal(["Update check: no newer version"], logLines);
    }

    [Fact]
    public async Task CheckAsync_TimeoutNotReached_LookUpGetsAnOpenToken()
    {
        LookUpReturns(new UpdateCheckResult(null));

        await HangGuard.Bound(flow.CheckAsync());

        Assert.True(lookUpToken.CanBeCanceled);
        Assert.False(lookUpToken.IsCancellationRequested);
    }

    [Fact]
    public async Task CheckAsync_TimeoutAlreadyExpired_LookUpGetsACancelledToken()
    {
        var expired = Build(TimeSpan.Zero);
        LookUpReturns(new UpdateCheckResult(null), expired);

        await HangGuard.Bound(expired.CheckAsync());

        Assert.True(lookUpToken.IsCancellationRequested);
    }

    // ---- the offer ----

    [Fact]
    public async Task CheckAsync_OfferWithoutDialog_SaysAvailableAndInstallsNothing()
    {
        LookUpReturns(new UpdateCheckResult(Offer));
        InstallReturns(null);
        offerDialog = null;

        var answer = await HangGuard.Bound(flow.CheckAsync());

        Assert.Equal(string.Format(Strings.Update_Available, Offer.Version), answer);
        Assert.Equal([AvailableLine], logLines);
        Assert.Equal(0, installs);
        Assert.Empty(statuses);
    }

    [Fact]
    public async Task CheckAsync_OfferDeclined_SaysAvailableAndInstallsNothing()
    {
        LookUpReturns(new UpdateCheckResult(Offer));
        InstallReturns(null);
        var asked = new List<string>();
        offerDialog = offer =>
        {
            asked.Add(offer.Version);
            return Task.FromResult(false);
        };

        var answer = await HangGuard.Bound(flow.CheckAsync());

        Assert.Equal([Offer.Version], asked);
        Assert.Equal(string.Format(Strings.Update_Available, Offer.Version), answer);
        Assert.Equal(0, installs);
        Assert.Empty(statuses);
    }

    [Fact]
    public async Task CheckAsync_OfferWithoutInstall_AsksNothingSaysAvailableAndShowsNoCard()
    {
        LookUpReturns(new UpdateCheckResult(Offer));
        var asked = 0;
        offerDialog = _ =>
        {
            asked++;
            return Task.FromResult(true);
        };

        var answer = await HangGuard.Bound(flow.CheckAsync());

        Assert.Equal(0, asked);
        Assert.Equal(string.Format(Strings.Update_Available, Offer.Version), answer);
        Assert.Empty(statuses);
        Assert.Empty(shownProgress);
    }

    [Fact]
    public async Task CheckAsync_OfferWithoutInstall_LogsOnlyTheAvailableLine()
    {
        LookUpReturns(new UpdateCheckResult(Offer));
        offerDialog = _ => Task.FromResult(true);

        await HangGuard.Bound(flow.CheckAsync());

        Assert.Equal([AvailableLine], logLines);
        Assert.Empty(warnings);
    }

    [Fact]
    public async Task CheckAsync_OfferWithoutInstallAndWithoutDialog_SaysAvailable()
    {
        LookUpReturns(new UpdateCheckResult(Offer));
        offerDialog = null;

        var answer = await HangGuard.Bound(flow.CheckAsync());

        Assert.Equal(string.Format(Strings.Update_Available, Offer.Version), answer);
        Assert.Equal([AvailableLine], logLines);
    }

    [Fact]
    public async Task CheckAsync_InstallClearedBetweenTwoChecks_TheSecondAsksNothing()
    {
        LookUpReturns(new UpdateCheckResult(Offer));
        InstallReturns("disk full");
        var asked = 0;
        offerDialog = _ =>
        {
            asked++;
            return Task.FromResult(true);
        };
        await HangGuard.Bound(flow.CheckAsync());
        flow.Install = null;

        var answer = await HangGuard.Bound(flow.CheckAsync());

        Assert.Equal(1, asked);
        Assert.Equal(1, installs);
        Assert.Equal(string.Format(Strings.Update_Available, Offer.Version), answer);
    }

    [Fact]
    public async Task Use_NoInstaller_CheckAsyncAsksNothing()
    {
        var http = new FakeHttp(_ => FakeHttp.Json(Release("deploy-bbbbbbb")));
        var asked = 0;
        offerDialog = _ =>
        {
            asked++;
            return Task.FromResult(true);
        };
        flow.Use(Checker(http, isCi: true), null);

        var answer = await HangGuard.Bound(flow.CheckAsync());

        Assert.Single(http.Requests);
        Assert.Equal(0, asked);
        Assert.Equal(string.Format(Strings.Update_Available, "280926.0b2c"), answer);
    }

    [Fact]
    public async Task CheckAtStartAsync_NoInstaller_AsksNothing()
    {
        LookUpReturns(new UpdateCheckResult(Offer));
        checkAtStart = true;
        var asked = 0;
        offerDialog = _ =>
        {
            asked++;
            return Task.FromResult(true);
        };

        await HangGuard.Bound(flow.CheckAtStartAsync());

        Assert.Equal(1, lookUps);
        Assert.Equal(0, asked);
        Assert.Equal([AvailableLine], logLines);
    }

    // ---- the install ----

    [Fact]
    public async Task CheckAsync_OfferAcceptedAndInstalled_ShowsEveryReportThenClearsTheCardAndSaysStarting()
    {
        LookUpReturns(new UpdateCheckResult(Offer));
        var halfway = new UpdateProgress(Offer.Version, UpdatePhase.Downloading, 10, 20);
        InstallReturns(null, halfway);
        offerDialog = _ => Task.FromResult(true);
        var updatingWhileShown = new List<bool>();
        flow.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(UpdateFlow.Progress)) updatingWhileShown.Add(flow.IsUpdating);
        };

        var answer = await HangGuard.Bound(flow.CheckAsync());

        Assert.Equal(Strings.Update_Starting, answer);
        Assert.Equal([new UpdateProgress(Offer.Version, UpdatePhase.Downloading), halfway, null], shownProgress);
        Assert.Equal([true, true, false], updatingWhileShown);
        Assert.Equal([string.Format(Strings.Update_Downloading, Offer.Version)], statuses);
        Assert.Equal([AvailableLine, $"Installing update {Offer.Version}"], logLines);
        Assert.Empty(warnings);
    }

    [Fact]
    public async Task CheckAsync_InstallFails_ReportsTheFailureOnEveryChannelAndClearsTheCard()
    {
        LookUpReturns(new UpdateCheckResult(Offer));
        InstallReturns("disk full");
        offerDialog = _ => Task.FromResult(true);

        var answer = await HangGuard.Bound(flow.CheckAsync());

        Assert.Equal("disk full", answer);
        Assert.Equal("disk full", logLines[^1]);
        Assert.Equal("disk full", statuses[^1]);
        Assert.Equal(["disk full"], warnings);
        Assert.Null(shownProgress[^1]);
    }

    [Fact]
    public async Task CheckAsync_InstallThrows_ClearsTheCardAndLetsTheErrorThrough()
    {
        LookUpReturns(new UpdateCheckResult(Offer));
        flow.Install = (_, _) => throw new InvalidOperationException("boom");
        offerDialog = _ => Task.FromResult(true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => HangGuard.Bound(flow.CheckAsync()));

        Assert.Equal([new UpdateProgress(Offer.Version, UpdatePhase.Downloading), null], shownProgress);
        Assert.False(flow.IsUpdating);
    }

    // ---- the start ----

    [Fact]
    public async Task CheckAtStartAsync_SettingOn_Checks()
    {
        LookUpReturns(new UpdateCheckResult(null));
        checkAtStart = true;

        await HangGuard.Bound(flow.CheckAtStartAsync());

        Assert.Equal(1, lookUps);
    }

    [Fact]
    public async Task CheckAtStartAsync_SettingOff_DoesNotCheck()
    {
        LookUpReturns(new UpdateCheckResult(null));
        checkAtStart = false;

        await HangGuard.Bound(flow.CheckAtStartAsync());

        Assert.Equal(0, lookUps);
    }

    // ---- the card ----

    [Fact]
    public void IsUpdating_ProgressSet_IsTrueAndTellsTheBindings()
    {
        flow.Progress = new UpdateProgress(Offer.Version, UpdatePhase.Verifying);

        Assert.True(flow.IsUpdating);
        Assert.Contains(nameof(UpdateFlow.IsUpdating), changedProperties);
    }

    [Fact]
    public void IsUpdating_ProgressCleared_IsFalseAndTellsTheBindings()
    {
        flow.Progress = new UpdateProgress(Offer.Version, UpdatePhase.Verifying);
        changedProperties.Clear();

        flow.Progress = null;

        Assert.False(flow.IsUpdating);
        Assert.Contains(nameof(UpdateFlow.IsUpdating), changedProperties);
    }
}
