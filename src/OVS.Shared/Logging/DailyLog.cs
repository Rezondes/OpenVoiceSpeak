using System.Text;

namespace OVS.Shared.Logging;

/// <summary>
/// Daily log files named &lt;prefix&gt;&lt;yyyy-MM-dd&gt;.log below one root folder. Files older than the retention are
/// deleted at start and at each day change. Writing never throws: the first failure goes to onFailure, later ones are dropped.
/// </summary>
public sealed class DailyLog
{
    readonly string root;
    readonly int keepDays;
    readonly TimeProvider time;
    readonly Action<string> onFailure;
    readonly object gate = new();
    DateOnly cleanedUpFor;
    bool failureReported;

    /// <param name="keepDays">0 keeps every file.</param>
    public DailyLog(string root, int keepDays, TimeProvider time, Action<string> onFailure)
    {
        this.root = root;
        this.keepDays = keepDays;
        this.time = time;
        this.onFailure = onFailure;
        lock (gate) CleanUp(Today());
    }

    public string Stamp() => time.GetLocalNow().ToString("yyyy-MM-dd HH:mm:ss.fff");

    DateOnly Today() => DateOnly.FromDateTime(time.GetLocalNow().DateTime);

    /// <param name="folder">Relative to the root, "" for the root itself.</param>
    public void Append(string folder, string prefix, string line)
    {
        lock (gate)
        {
            try
            {
                var today = Today();
                if (today != cleanedUpFor) CleanUp(today);
                var dir = Path.Combine(root, folder);
                Directory.CreateDirectory(dir);
                AppendShared(Path.Combine(dir, $"{prefix}{today:yyyy-MM-dd}.log"), line + Environment.NewLine);
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
                if (name.Length >= 10 && DateOnly.TryParseExact(name[^10..], "yyyy-MM-dd", out var day) && day < oldest)
                    File.Delete(file);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // an undeletable old file is not worth failing over; the next day change tries again
        }
    }
}
