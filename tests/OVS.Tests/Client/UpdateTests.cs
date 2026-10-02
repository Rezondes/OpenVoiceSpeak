using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OVS.Client.Net;
using OVS.Shared;

namespace OVS.Tests.Client;

/// <summary>Answers every request with the given function and remembers what was asked.</summary>
sealed class FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<Uri> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        return Task.FromResult(respond(request));
    }

    public static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    public static HttpResponseMessage Bytes(byte[] value) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(value) };
}

/// <summary>A download whose size is unknown up front, as with a chunked response.</summary>
sealed class Unseekable(byte[] data) : MemoryStream(data)
{
    public override bool CanSeek => false;
}

/// <summary>Package 43: which release counts as an update, and how the exe is swapped.</summary>
public sealed class UpdateTests : IDisposable
{
    static readonly DateTimeOffset Built = new(2026, 9, 28, 8, 0, 0, TimeSpan.Zero);
    static readonly BuildInfo Running = new(Built, "aaaaaaa1111111", IsCi: true);
    readonly string dir = Directory.CreateTempSubdirectory("ovs-update-").FullName;

    public void Dispose() => Directory.Delete(dir, true);

    static object Release(string tag, DateTimeOffset published, bool withHash = true) => new
    {
        tag_name = tag,
        name = "OpenVoiceSpeak 280926.0b2c",
        body = "- Neue Funktion (bbbbbbb)",
        published_at = published,
        assets = new[]
        {
            new { name = "OVS.Client.exe", browser_download_url = "https://example.org/OVS.Client.exe" },
            new { name = withHash ? "OVS.Client.exe.sha256" : "andere.txt", browser_download_url = "https://example.org/OVS.Client.exe.sha256" },
        },
    };

    static Task<UpdateCheckResult> Check(object release, BuildInfo? running = null) =>
        new UpdateChecker(new HttpClient(new FakeHttp(_ => FakeHttp.Json(release))), running ?? Running).CheckAsync();

    [Fact]
    public async Task NewerRelease_Offered()
    {
        var offer = (await Check(Release("deploy-bbbbbbb", Built.AddHours(1)))).Offer;
        Assert.NotNull(offer);
        Assert.Equal(("280926.0b2c", "deploy-bbbbbbb", "https://example.org/OVS.Client.exe"), (offer.Version, offer.Tag, offer.ExeUrl.ToString()));
        Assert.Contains("Neue Funktion", offer.Notes);
    }

    [Fact]
    public async Task SameCommit_OlderRelease_MissingAsset_NotOffered()
    {
        Assert.Null((await Check(Release("deploy-aaaaaaa", Built.AddHours(1)))).Offer); // this very build
        Assert.Null((await Check(Release("deploy-bbbbbbb", Built.AddHours(-1)))).Offer); // older than this build
        Assert.Null((await Check(Release("deploy-bbbbbbb", Built.AddHours(1), withHash: false))).Offer);
    }

    [Fact]
    public async Task DevBuild_NeverAsks()
    {
        var http = new FakeHttp(_ => throw new InvalidOperationException("darf nicht fragen"));
        var result = await new UpdateChecker(new HttpClient(http), Running with { IsCi = false }).CheckAsync();
        Assert.Equal((null, null), (result.Offer, result.Error));
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task HttpErrorOrNoNetwork_NoUpdate_WithReason()
    {
        var limited = await new UpdateChecker(new HttpClient(new FakeHttp(_ => new HttpResponseMessage(HttpStatusCode.Forbidden))), Running).CheckAsync();
        Assert.Null(limited.Offer);
        Assert.Contains("403", limited.Error);
        var offline = await new UpdateChecker(new HttpClient(new FakeHttp(_ => throw new HttpRequestException("kein Netz"))), Running).CheckAsync();
        Assert.Equal("kein Netz", offline.Error);
    }

    // ---- installer ----

    static readonly UpdateOffer Offer = new("280926.0b2c", "deploy-bbbbbbb", "", Built.AddHours(1),
        new Uri("https://example.org/OVS.Client.exe"), new Uri("https://example.org/OVS.Client.exe.sha256"));

    (UpdateInstaller Installer, string Exe, List<string> Restarted) Installer(byte[] served, string hash, bool exeFails = false)
    {
        var exe = Path.Combine(dir, "OVS.Client.exe");
        File.WriteAllText(exe, "alt");
        var restarted = new List<string>();
        var http = new FakeHttp(r => r.RequestUri!.AbsolutePath.EndsWith(".sha256")
            ? FakeHttp.Bytes(Encoding.ASCII.GetBytes($"{hash}  OVS.Client.exe\n"))
            : exeFails ? new HttpResponseMessage(HttpStatusCode.InternalServerError) : FakeHttp.Bytes(served));
        return (new UpdateInstaller(new HttpClient(http), exe, restarted.Add), exe, restarted);
    }

    static string Sha(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    [Fact]
    public async Task Success_NewInPlace_OldRenamed_Restarted()
    {
        var neu = Encoding.ASCII.GetBytes("neu");
        var (installer, exe, restarted) = Installer(neu, Sha(neu));
        Assert.Null(await installer.InstallAsync(Offer));
        Assert.Equal("neu", File.ReadAllText(exe));
        Assert.Equal("alt", File.ReadAllText(exe + ".old"));
        Assert.Equal([exe], restarted);

        Assert.Equal("Alte Version entfernt", await UpdateInstaller.CleanupOldAsync(exe)); // next start
        Assert.False(File.Exists(exe + ".old"));
        Assert.Null(await UpdateInstaller.CleanupOldAsync(exe)); // nothing left to do
    }

    [Fact]
    public async Task HashMismatch_NothingReplaced()
    {
        var (installer, exe, restarted) = Installer(Encoding.ASCII.GetBytes("manipuliert"), Sha(Encoding.ASCII.GetBytes("neu")));
        Assert.Contains("Prüfsumme", await installer.InstallAsync(Offer));
        Assert.Equal("alt", File.ReadAllText(exe));
        Assert.Equal([Path.GetFileName(exe)], Directory.GetFiles(dir).Select(Path.GetFileName));
        Assert.Empty(restarted);
    }

    [Fact]
    public async Task DownloadFails_ExeUnchanged()
    {
        var (installer, exe, restarted) = Installer([], "", exeFails: true);
        Assert.Contains("nicht installiert", await installer.InstallAsync(Offer));
        Assert.Equal("alt", File.ReadAllText(exe));
        Assert.Empty(restarted);
    }

    // ---- progress (Package 62) ----

    [Fact]
    public async Task Install_ReportsProgress_ThenVerify_ThenStart()
    {
        var neu = new byte[300_000];
        new Random(1).NextBytes(neu);
        var (installer, _, _) = Installer(neu, Sha(neu));
        var reports = new List<UpdateProgress>();
        Assert.Null(await installer.InstallAsync(Offer, reports.Add));

        var loading = reports.TakeWhile(r => r.Phase == UpdatePhase.Downloading).ToList();
        Assert.True(loading.Count > 1, $"{loading.Count} Meldungen");
        Assert.All(loading, r => Assert.Equal(neu.Length, r.Total));
        Assert.Equal(loading.Select(r => r.Bytes).Order(), loading.Select(r => r.Bytes));
        Assert.Equal(neu.Length, loading[^1].Bytes);
        Assert.Equal(100, loading[^1].Percent);
        Assert.False(loading[^1].IsIndeterminate);
        Assert.Equal([UpdatePhase.Verifying, UpdatePhase.Starting], reports.Skip(loading.Count).Select(r => r.Phase));
    }

    [Fact]
    public async Task Install_WithoutLength_ReportsUnknownTotal()
    {
        var neu = Encoding.ASCII.GetBytes("neu ohne Laenge");
        var exe = Path.Combine(dir, "OVS.Client.exe");
        File.WriteAllText(exe, "alt");
        var http = new FakeHttp(r => r.RequestUri!.AbsolutePath.EndsWith(".sha256")
            ? FakeHttp.Bytes(Encoding.ASCII.GetBytes(Sha(neu)))
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new Unseekable(neu)) });
        var reports = new List<UpdateProgress>();
        Assert.Null(await new UpdateInstaller(new HttpClient(http), exe, _ => { }).InstallAsync(Offer, reports.Add));

        var loading = reports.Where(r => r.Phase == UpdatePhase.Downloading).ToList();
        Assert.NotEmpty(loading);
        Assert.All(loading, r => Assert.Null(r.Total));
        Assert.True(loading[^1].IsIndeterminate);
        Assert.Equal(neu.Length, loading[^1].Bytes);
        Assert.Equal("neu ohne Laenge", File.ReadAllText(exe));
    }

    // ---- restart and cleanup (Package 63) ----

    /// <summary>A77: the restart threw (in the real client: Process could not be loaded from the renamed exe) and took the client down.</summary>
    [Fact]
    public async Task RestartFails_ClientStaysOpen_ErrorShown()
    {
        var neu = Encoding.ASCII.GetBytes("neu");
        var exe = Path.Combine(dir, "OVS.Client.exe");
        File.WriteAllText(exe, "alt");
        var http = new FakeHttp(r => FakeHttp.Bytes(r.RequestUri!.AbsolutePath.EndsWith(".sha256") ? Encoding.ASCII.GetBytes(Sha(neu)) : neu));
        var installer = new UpdateInstaller(new HttpClient(http), exe, _ => throw new FileNotFoundException("System.Diagnostics.Process fehlt"));

        var result = await installer.InstallAsync(Offer);
        Assert.NotNull(result);
        Assert.Contains("System.Diagnostics.Process fehlt", result);
        Assert.Equal("neu", File.ReadAllText(exe)); // installed; the next start by hand uses it
    }

    [Fact]
    public void Restart_PassesArgs_PlusAfterUpdatePid()
    {
        Assert.Equal(["--profile", "X", "--after-update", "42"], UpdateInstaller.RestartArgs(["--profile", "X"], 42));
        Assert.Equal(["--no-audio", "--after-update", "42"], UpdateInstaller.RestartArgs(["--after-update", "7", "--no-audio"], 42));
    }

    [Fact]
    public void Parse_AfterUpdate()
    {
        Assert.Equal(1234, OVS.Client.ClientOptions.Parse(["--after-update", "1234", "--no-audio"]).AfterUpdatePid);
        var invalid = OVS.Client.ClientOptions.Parse(["--after-update", "abc"]);
        Assert.Null(invalid.AfterUpdatePid);
        Assert.Null(OVS.Client.ClientOptions.Parse([]).AfterUpdatePid);
    }

    [Fact]
    public async Task CleanupOld_WaitsForLockedFile_ThenDeletes()
    {
        var exe = Path.Combine(dir, "OVS.Client.exe");
        File.WriteAllText(exe + ".old", "alt");
        var locked = new FileStream(exe + ".old", FileMode.Open, FileAccess.Read, FileShare.None);
        _ = Task.Delay(700).ContinueWith(_ => locked.Dispose());

        // up to 10 s of retries: the release above may come late on a busy runner, it still has to wait for it
        Assert.Equal("Alte Version entfernt", await UpdateInstaller.CleanupOldAsync(exe, attempts: 100, pause: TimeSpan.FromMilliseconds(100)));
        Assert.False(File.Exists(exe + ".old"));
    }

    [Fact]
    public async Task CleanupOld_StillLocked_ReportsReason()
    {
        var exe = Path.Combine(dir, "OVS.Client.exe");
        File.WriteAllText(exe + ".old", "alt");
        using var locked = new FileStream(exe + ".old", FileMode.Open, FileAccess.Read, FileShare.None);
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var result = await UpdateInstaller.CleanupOldAsync(exe, attempts: 3, pause: TimeSpan.FromMilliseconds(100));
        Assert.StartsWith("Alte Version konnte nicht entfernt werden", result);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), clock.Elapsed.ToString()); // gives up instead of hanging (with room for a slow runner)
        Assert.True(File.Exists(exe + ".old"));
    }

    [Fact]
    public async Task CleanupOld_WaitsForProcessExit()
    {
        var exe = Path.Combine(dir, "OVS.Client.exe");
        File.WriteAllText(exe + ".old", "alt");
        using var old = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c ping -n 2 127.0.0.1 >nul")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;

        Assert.Equal("Alte Version entfernt", await UpdateInstaller.CleanupOldAsync(exe, old.Id));
        Assert.True(old.HasExited); // deleted only after the old process was gone
        Assert.False(File.Exists(exe + ".old"));
    }
}
