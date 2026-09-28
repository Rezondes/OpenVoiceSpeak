using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using OVS.Shared;

namespace OVS.Client.Net;

/// <summary>A newer release than the running build, with its exe and the exe's SHA-256 file.</summary>
public sealed record UpdateOffer(string Version, string Tag, string Notes, DateTimeOffset PublishedAt, Uri ExeUrl, Uri HashUrl);

/// <summary>Offer is null when there is nothing newer; Error says why the check did not work.</summary>
public sealed record UpdateCheckResult(UpdateOffer? Offer, string? Error = null);

/// <summary>
/// Package 43: asks GitHub for the newest release (the repository is public, A39). Only CI builds ask, and only for
/// a release with another commit that came out after this build and carries both files. Nothing but this read
/// request leaves the client; no user data is sent.
/// </summary>
public sealed class UpdateChecker(HttpClient http, BuildInfo current)
{
    public const string LatestReleaseUrl = "https://api.github.com/repos/Rezondes/OpenVoiceSpeak/releases/latest";
    public const string ExeName = "OVS.Client.exe";
    const string TitlePrefix = "OpenVoiceSpeak ";

    public bool IsEnabled => current.IsCi;

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancel = default)
    {
        if (!IsEnabled) return new UpdateCheckResult(null);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("OpenVoiceSpeak", current.Version));
            using var response = await http.SendAsync(request, cancel);
            if (!response.IsSuccessStatusCode) return new UpdateCheckResult(null, $"GitHub antwortet mit {(int)response.StatusCode}.");
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancel), cancellationToken: cancel);
            return new UpdateCheckResult(Evaluate(json.RootElement));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return new UpdateCheckResult(null, e.Message);
        }
    }

    UpdateOffer? Evaluate(JsonElement release)
    {
        var tag = release.GetProperty("tag_name").GetString() ?? "";
        if (current.ShortCommit.Length > 0 && tag == "deploy-" + current.ShortCommit) return null; // this very build
        var published = release.GetProperty("published_at").GetDateTimeOffset();
        if (published <= current.BuildTime) return null;
        var assets = release.GetProperty("assets").EnumerateArray()
            .ToDictionary(a => a.GetProperty("name").GetString() ?? "", a => a.GetProperty("browser_download_url").GetString() ?? "");
        if (!assets.TryGetValue(ExeName, out var exe) || !assets.TryGetValue(ExeName + ".sha256", out var hash)) return null;
        var title = release.TryGetProperty("name", out var name) ? name.GetString() ?? tag : tag;
        var notes = release.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";
        return new UpdateOffer(title.StartsWith(TitlePrefix) ? title[TitlePrefix.Length..] : title, tag, notes.Trim(), published, new Uri(exe), new Uri(hash));
    }
}

/// <summary>
/// Package 43: downloads the new exe next to the running one, checks it against the published SHA-256, then swaps
/// them. Windows lets a running exe be renamed but not overwritten, so the old one becomes "*.old" and is removed
/// on the next start. Any failure leaves the running exe as it was.
/// </summary>
public sealed class UpdateInstaller(HttpClient http, string exePath, Action<string> restart)
{
    string NewPath => exePath + ".new";
    string OldPath => exePath + ".old";

    /// <returns>Null when the new version was started, otherwise why not.</returns>
    public async Task<string?> InstallAsync(UpdateOffer offer, CancellationToken cancel = default)
    {
        try
        {
            var expected = (await http.GetStringAsync(offer.HashUrl, cancel)).Split(' ', '\t', '\r', '\n')[0].Trim().ToLowerInvariant();
            await using (var source = await http.GetStreamAsync(offer.ExeUrl, cancel))
            await using (var target = File.Create(NewPath))
                await source.CopyToAsync(target, cancel);
            string actual;
            await using (var file = File.OpenRead(NewPath))
                actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, cancel));
            if (actual != expected)
            {
                File.Delete(NewPath);
                return "Das Update ist beschädigt angekommen (Prüfsumme stimmt nicht). Die bisherige Version bleibt.";
            }
            if (File.Exists(OldPath)) File.Delete(OldPath);
            File.Move(exePath, OldPath);
            File.Move(NewPath, exePath);
        }
        catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException)
        {
            TryDelete(NewPath);
            if (!File.Exists(exePath) && File.Exists(OldPath)) File.Move(OldPath, exePath); // swap half done: undo it
            return $"Das Update konnte nicht installiert werden: {e.Message}";
        }
        restart(exePath);
        return null;
    }

    /// <summary>At start: the exe replaced by the last update is not running any more and can go.</summary>
    public static void CleanupOld(string? exePath)
    {
        if (exePath is not null) TryDelete(exePath + ".old");
    }

    static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
