using System.Globalization;
using OVS.Client.Localization;
using System.Security.Cryptography;
using OVS.Shared.IO;

namespace OVS.Client.Net.Updates;

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
    /// <param name="offer">The release to install.</param>
    /// <param name="cancel">Ends the download.</param>
    public async Task<string?> InstallAsync(UpdateOffer offer, Action<UpdateProgress>? progress = null, CancellationToken cancel = default)
    {
        try
        {
            var expected = (await http.GetStringAsync(offer.HashUrl, cancel)).Split(' ', '\t', '\r', '\n')[0].Trim();
            await DownloadAsync(offer, progress, cancel);
            progress?.Invoke(new UpdateProgress(offer.Version, UpdatePhase.Verifying));
            string actual;
            await using (var file = File.OpenRead(NewPath))
                actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, cancel));
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(NewPath);
                return Strings.Update_Damaged;
            }
            if (File.Exists(OldPath)) File.Delete(OldPath);
            File.Move(exePath, OldPath);
            File.Move(NewPath, exePath);
            progress?.Invoke(new UpdateProgress(offer.Version, UpdatePhase.Starting));
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException)
        {
            if (File.Exists(NewPath)) SafeFile.DeleteQuietly(NewPath);
            if (!File.Exists(exePath) && File.Exists(OldPath)) File.Move(OldPath, exePath); // swap half done: undo it
            return string.Format(CultureInfo.CurrentCulture, Strings.Update_InstallFailed, exception.Message);
        }
        try
        {
            restart(exePath);
        }
        catch (Exception exception) // Package 63: whatever it is, the window stays open and says so instead of vanishing
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.Update_RestartFailed, exception.Message);
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
        using var enumerator = current.GetEnumerator();
        while (enumerator.MoveNext())
        {
            if (enumerator.Current == ClientOptions.AfterUpdateArg) enumerator.MoveNext();
            else args.Add(enumerator.Current);
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
        long step = total is > 0 and var totalBytes ? Math.Max(1, totalBytes / 100) : long.MaxValue;
        var clock = System.Diagnostics.Stopwatch.StartNew(); // clock: real time, how often the download reports its progress
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
    /// <param name="time">The clock of the pauses between the tries (the wait for the old process is real time).</param>
    /// <returns>What happened, for the log; null when there was nothing to remove.</returns>
    /// <param name="exePath">The running exe, null when unknown.</param>
    /// <param name="oldPid">The old process, from the after-update argument.</param>
    /// <param name="attempts">How often the removal is tried.</param>
    /// <param name="pause">The pause between the tries.</param>
    /// <param name="waitForExit">The longest wait for the old process, a default when null.</param>
    public static async Task<string?> CleanupOldAsync(string? exePath, TimeProvider time, int? oldPid = null, TimeSpan? waitForExit = null,
        int attempts = 5, TimeSpan? pause = null)
    {
        if (exePath is null || !File.Exists(exePath + ".old")) return null;
        if (oldPid is { } pid)
        {
            try
            {
                using var old = System.Diagnostics.Process.GetProcessById(pid);
                using var timeout = new CancellationTokenSource(waitForExit ?? TimeSpan.FromSeconds(10)); // clock: real time
                await old.WaitForExitAsync(timeout.Token);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OperationCanceledException)
            {
                // already gone, or still there after the wait: try anyway
            }
        }
        for (int index = 1; ; index++)
        {
            try
            {
                File.Delete(exePath + ".old");
                return "Old version removed";
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (index >= attempts) return $"Old version could not be removed: {exception.Message}";
                await Task.Delay(pause ?? TimeSpan.FromMilliseconds(500), time);
            }
        }
    }
}
