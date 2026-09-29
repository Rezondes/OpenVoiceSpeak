using OVS.Client.Localization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using OVS.Shared;

namespace OVS.Client.Net;

/// <summary>A newer release than the running build, with its exe and the exe's SHA-256 file.</summary>
public sealed record UpdateOffer(string Version, string Tag, string Notes, DateTimeOffset PublishedAt, Uri ExeUrl, Uri HashUrl);

public enum UpdatePhase { Downloading, Verifying, Starting }

/// <summary>Package 62: what the update card over the window shows. Total is null when GitHub sends no size.</summary>
public sealed record UpdateProgress(string Version, UpdatePhase Phase, long Bytes = 0, long? Total = null)
{
    const double MB = 1024 * 1024;

    public bool IsIndeterminate => Phase != UpdatePhase.Downloading || Total is not > 0;
    public double Percent => Total is > 0 and var total ? Math.Min(100, 100.0 * Bytes / total) : 0;
    public string Title => string.Format(Strings.Update_Title, Version);

    public string Detail => Phase switch
    {
        UpdatePhase.Verifying => Strings.Update_Verifying,
        UpdatePhase.Starting => Strings.Update_Starting,
        _ when Total is > 0 and var total => string.Format(Strings.Update_Size, Percent, Bytes / MB, total / MB),
        _ => string.Format(Strings.Update_SizeUnknown, Bytes / MB),
    };
}

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
            if (!response.IsSuccessStatusCode) return new UpdateCheckResult(null, string.Format(Strings.Update_GitHubStatus, (int)response.StatusCode));
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
    /// <param name="progress">Package 62: called on the caller's context, at most every 1 % or 100 ms while loading.</param>
    public async Task<string?> InstallAsync(UpdateOffer offer, Action<UpdateProgress>? progress = null, CancellationToken cancel = default)
    {
        try
        {
            var expected = (await http.GetStringAsync(offer.HashUrl, cancel)).Split(' ', '\t', '\r', '\n')[0].Trim().ToLowerInvariant();
            await DownloadAsync(offer, progress, cancel);
            progress?.Invoke(new UpdateProgress(offer.Version, UpdatePhase.Verifying));
            string actual;
            await using (var file = File.OpenRead(NewPath))
                actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, cancel));
            if (actual != expected)
            {
                File.Delete(NewPath);
                return Strings.Update_Damaged;
            }
            if (File.Exists(OldPath)) File.Delete(OldPath);
            File.Move(exePath, OldPath);
            File.Move(NewPath, exePath);
            progress?.Invoke(new UpdateProgress(offer.Version, UpdatePhase.Starting));
        }
        catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException)
        {
            TryDelete(NewPath);
            if (!File.Exists(exePath) && File.Exists(OldPath)) File.Move(OldPath, exePath); // swap half done: undo it
            return string.Format(Strings.Update_InstallFailed, e.Message);
        }
        try
        {
            restart(exePath);
        }
        catch (Exception e) // Package 63: whatever it is, the window stays open and says so instead of vanishing
        {
            return string.Format(Strings.Update_RestartFailed, e.Message);
        }
        return null;
    }

    /// <summary>
    /// Package 63: the arguments for the new version, the running ones plus "--after-update &lt;pid&gt;" so it waits for
    /// this process before it removes "*.old". An earlier "--after-update" is not passed on twice.
    /// </summary>
    public static List<string> RestartArgs(IEnumerable<string> current, int pid)
    {
        var args = new List<string>();
        using var e = current.GetEnumerator();
        while (e.MoveNext())
        {
            if (e.Current == ClientOptions.AfterUpdateArg) e.MoveNext();
            else args.Add(e.Current);
        }
        args.AddRange([ClientOptions.AfterUpdateArg, pid.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        return args;
    }

    async Task DownloadAsync(UpdateOffer offer, Action<UpdateProgress>? progress, CancellationToken cancel)
    {
        using var response = await http.GetAsync(offer.ExeUrl, HttpCompletionOption.ResponseHeadersRead, cancel);
        response.EnsureSuccessStatusCode();
        long? total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancel);
        await using var target = File.Create(NewPath);
        var buffer = new byte[81920];
        long bytes = 0, reported = -1;
        long step = total is > 0 and var t ? Math.Max(1, t / 100) : long.MaxValue;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        int read;
        while ((read = await source.ReadAsync(buffer, cancel)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancel);
            bytes += read;
            if (bytes - reported < step && clock.ElapsedMilliseconds < 100) continue;
            progress?.Invoke(new UpdateProgress(offer.Version, UpdatePhase.Downloading, bytes, total));
            reported = bytes;
            clock.Restart();
        }
        if (bytes != reported) progress?.Invoke(new UpdateProgress(offer.Version, UpdatePhase.Downloading, bytes, total));
    }

    /// <summary>
    /// At start: removes the exe replaced by the last update. Package 63: right after an update the old process may
    /// still be closing, and Windows keeps a just-ended exe locked for a moment, so it waits for that process (at
    /// most <paramref name="waitForExit"/>) and tries a few times.
    /// </summary>
    /// <returns>What happened, for the log; null when there was nothing to remove.</returns>
    public static async Task<string?> CleanupOldAsync(string? exePath, int? oldPid = null, TimeSpan? waitForExit = null,
        int attempts = 5, TimeSpan? pause = null)
    {
        if (exePath is null || !File.Exists(exePath + ".old")) return null;
        if (oldPid is { } pid)
        {
            try
            {
                using var old = System.Diagnostics.Process.GetProcessById(pid);
                using var timeout = new CancellationTokenSource(waitForExit ?? TimeSpan.FromSeconds(10));
                await old.WaitForExitAsync(timeout.Token);
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or OperationCanceledException)
            {
                // already gone, or still there after the wait: try anyway
            }
        }
        for (int i = 1; ; i++)
        {
            try
            {
                File.Delete(exePath + ".old");
                return "Alte Version entfernt";
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (i >= attempts) return $"Alte Version konnte nicht entfernt werden: {e.Message}";
                await Task.Delay(pause ?? TimeSpan.FromMilliseconds(500));
            }
        }
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
