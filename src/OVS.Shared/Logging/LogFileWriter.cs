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
public sealed class LogFileWriter
{
    /// <summary>The start of a log file in its name, in local time; LogReader on the server reads it back.</summary>
    public const string NameFormat = "yyyy-MM-dd_HH-mm-ss";

    readonly string root;
    int keepDays;
    readonly TimeProvider time;
    readonly Action<string> onFailure;
    bool newFileEachDay;
    readonly object gate = new();
    DateOnly fileDay, cleanedUpFor;
    string fileName = "";
    bool failureReported;
    readonly Dictionary<string, StringBuilder> waiting = []; // text a reader's lock kept out, per file, in order
    ITimer? retry;
    int retries;

    /// <summary>How often lines a reader's lock kept out are tried again.</summary>
    static readonly TimeSpan RetryEvery = TimeSpan.FromMilliseconds(20);

    /// <summary>Tries before waiting lines are given up and the failure reported: 5 s.</summary>
    const int MaxRetries = 250;

    /// <param name="keepDays">0 keeps every file.</param>
    /// <param name="root">The logs folder.</param>
    /// <param name="time">The clock of the time stamps and file names.</param>
    /// <param name="onFailure">Told once when a write fails, with the reason.</param>
    /// <param name="newFileEachDay">True starts a new file at midnight, false keeps one file per start.</param>
    public LogFileWriter(string root, int keepDays, TimeProvider time, Action<string> onFailure, bool newFileEachDay = true)
    {
        this.root = root;
        this.keepDays = keepDays;
        this.time = time;
        this.onFailure = onFailure;
        this.newFileEachDay = newFileEachDay;
        lock (gate) StartFile(time.GetLocalNow());
    }

    /// <summary>Package 69: changes the settings of a running log. The retention applies at the next cleanup, which
    /// runs with the next line; the daily files from the next day change on.</summary>
    public void Update(int keepDays, bool newFileEachDay)
    {
        lock (gate)
        {
            if (keepDays != this.keepDays) cleanedUpFor = default;
            this.keepDays = keepDays;
            this.newFileEachDay = newFileEachDay;
        }
    }

    /// <summary>The time stamp of a line written now, in local time.</summary>
    public string FormatNow() => time.GetLocalNow().ToString("yyyy-MM-dd HH:mm:ss.fff");

    void StartFile(DateTimeOffset now)
    {
        fileDay = DateOnly.FromDateTime(now.DateTime);
        fileName = now.ToString(NameFormat, CultureInfo.InvariantCulture);
        CleanUp(fileDay);
    }

    /// <param name="folder">Relative to the root, "" for the root itself.</param>
    /// <param name="prefix">Put before the file name.</param>
    /// <param name="line">The text after the time stamp.</param>
    public void Append(string folder, string prefix, string line) => Append(folder, prefix, [line]);

    /// <summary>Package 124: several lines with one opening of the file.</summary>
    public void Append(string folder, string prefix, IReadOnlyList<string> lines)
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
                var text = new StringBuilder();
                foreach (var line in lines) text.Append(EscapeToOneLine(line)).Append(Environment.NewLine);
                Write(Path.Combine(dir, $"{prefix}{fileName}.log"), text.ToString());
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                ReportFailure(exception.Message);
            }
        }
    }

    /// <summary>Appends the text, or keeps it behind what already waits for that file until the retry timer gets it in.</summary>
    void Write(string path, string text)
    {
        if (waiting.TryGetValue(path, out var queued))
        {
            queued.Append(text);
            return;
        }
        if (TryAppendShared(path, text)) return;
        waiting[path] = new StringBuilder(text);
        retry ??= time.CreateTimer(_ => Retry(), null, RetryEvery, RetryEvery);
    }

    void Retry()
    {
        lock (gate)
        {
            try
            {
                foreach (var path in waiting.Keys.ToList())
                    if (TryAppendShared(path, waiting[path].ToString())) waiting.Remove(path);
                if (waiting.Count > 0 && ++retries < MaxRetries) return;
                if (waiting.Count > 0) ReportFailure($"{waiting.Count} file(s) locked");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                ReportFailure(exception.Message);
            }
            waiting.Clear();
            retry?.Dispose();
            retry = null;
            retries = 0;
        }
    }

    void ReportFailure(string reason)
    {
        if (failureReported) return;
        failureReported = true;
        onFailure(EscapeToOneLine($"{FormatNow()} Log could not be written: {reason}"));
    }

    /// <summary>
    /// One entry stays one line: line breaks and other control characters in logged text (chat, reasons) become
    /// visible escapes such as \n or \u0085, so a message cannot forge further, timestamped lines. Other text is unchanged.
    /// Package 90: also used for the server console; ESC (U+001B) and the C1 CSI (U+009B) are control characters as well,
    /// so no ANSI escape sequence reaches a terminal or docker logs.
    /// </summary>
    public static string EscapeToOneLine(string line)
    {
        if (!line.Any(IsBreaking)) return line;
        var text = new StringBuilder(line.Length + 16);
        foreach (var character in line)
        {
            if (character == '\r') text.Append(@"\r");
            else if (character == '\n') text.Append(@"\n");
            else if (character == '\t') text.Append(@"\t");
            else if (IsBreaking(character)) text.Append(@"\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
            else text.Append(character);
        }
        return text.ToString();
    }

    static bool IsBreaking(char character) => char.IsControl(character) || character is '\u2028' or '\u2029';

    /// <summary>
    /// Lets readers keep the file open. False while a reader locks it against writing (File.ReadAllText and many editors
    /// do), so the text can wait for <see cref="Retry"/> instead of being lost.
    /// </summary>
    static bool TryAppendShared(string path, string text)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            file.Write(Encoding.UTF8.GetBytes(text));
            return true;
        }
        catch (IOException) when (File.Exists(path))
        {
            return false;
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // an undeletable old file is not worth failing over; the next day change tries again
        }
    }
}
