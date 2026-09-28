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

        UpdateInstaller.CleanupOld(exe); // next start
        Assert.False(File.Exists(exe + ".old"));
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
}
