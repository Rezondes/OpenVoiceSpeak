using System.Globalization;
using System.Text;

namespace OVS.Shared.Logging;

/// <summary>
/// One log file per start: &lt;prefix&gt;&lt;yyyy-MM-dd_HH-mm-ss&gt;.log, so a problem is found in a small file.
/// Every folder of one run uses the same name, so the server log and the channel logs of a run belong together.
/// With newFileEachDay, a run past midnight starts a new file named after that moment; otherwise the file lasts until the next start.
/// Files older than the retention are deleted at start and at each day change, either way.
/// Writing never throws: the first failure goes to onFailure, later ones are dropped.
/// </summary>
public sealed class LogFiles
{
    const string NameFormat = "yyyy-MM-dd_HH-mm-ss";

    readonly string root;
    readonly int keepDays;
    readonly TimeProvider time;
    readonly Action<string> onFailure;
    readonly bool newFileEachDay;
    readonly object gate = new();
    DateOnly fileDay, cleanedUpFor;
    string fileName = "";
    bool failureReported;

    /// <param name="keepDays">0 keeps every file.</param>
    public LogFiles(string root, int keepDays, TimeProvider time, Action<string> onFailure, bool newFileEachDay = true)
    {
        this.root = root;
        this.keepDays = keepDays;
        this.time = time;
        this.onFailure = onFailure;
        this.newFileEachDay = newFileEachDay;
        lock (gate) StartFile(time.GetLocalNow());
    }

    public string Stamp() => time.GetLocalNow().ToString("yyyy-MM-dd HH:mm:ss.fff");

    void StartFile(DateTimeOffset now)
    {
        fileDay = DateOnly.FromDateTime(now.DateTime);
        fileName = now.ToString(NameFormat, CultureInfo.InvariantCulture);
        CleanUp(fileDay);
    }

    /// <param name="folder">Relative to the root, "" for the root itself.</param>
    public void Append(string folder, string prefix, string line)
    {
        lock (gate)
        {
            try
            {
                var now = time.GetLocalNow();
                var today = DateOnly.FromDateTime(now.DateTime);
                if (newFileEachDay && today != fileDay) StartFile(now);
                else if (today != cleanedUpFor) CleanUp(today);
                var dir = Path.Combine(root, folder);
                Directory.CreateDirectory(dir);
                AppendShared(Path.Combine(dir, $"{prefix}{fileName}.log"), line + Environment.NewLine);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (failureReported) return;
                failureReported = true;
                onFailure($"{Stamp()} Log konnte nicht geschrieben werden: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Lets readers keep the file open, and retries briefly while a reader locks it against writing
    /// (File.ReadAllText and many editors do), instead of losing the line.
    /// </summary>
    static void AppendShared(string path, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                file.Write(bytes);
                return;
            }
            catch (IOException) when (attempt < 5 && File.Exists(path))
            {
                Thread.Sleep(20);
            }
        }
    }

    /// <summary>Age counts from the start time in the file name. The files still being written are never deleted,
    /// even when a run without daily files lasts longer than the retention.</summary>
    void CleanUp(DateOnly today)
    {
        cleanedUpFor = today;
        if (keepDays <= 0 || !Directory.Exists(root)) return;
        var oldest = today.AddDays(-keepDays);
        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*.log", SearchOption.AllDirectories).ToList())
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (name.Length >= NameFormat.Length && !name.EndsWith(fileName, StringComparison.Ordinal)
                    && DateTime.TryParseExact(name[^NameFormat.Length..], NameFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var started)
                    && DateOnly.FromDateTime(started) < oldest)
                    File.Delete(file);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // an undeletable old file is not worth failing over; the next day change tries again
        }
    }
}
