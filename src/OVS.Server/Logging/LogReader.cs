using System.Globalization;
using System.Text;
using System.Text.Json;
using OVS.Shared.Logging;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Framing;
using OVS.Shared.Protocol.Messages.LogAccess;

namespace OVS.Server.Logging;

/// <summary>
/// Package 81 (A98): lists, pages and searches the log files ServerLogWriter writes. Only *.log files directly in
/// logs/server and logs/channels/&lt;guid&gt; exist for it, never links; a file is found only by the id of its own listing,
/// so no path from a client is ever opened. Thread-safe, meant to run off the state lock.
/// </summary>
internal sealed class LogReader(string dataDir, TimeProvider time)
{
    internal const string ServerFolder = "server";
    internal const string ChannelFolder = "channels";
    /// <summary>Put at the end of a line cut at MaxLogLineLength.</summary>
    public const string CutMarker = " [...]";
    // JSON bytes the lines of one answer may take; the rest (ids, numbers) stays well below FrameReader.MaxFrameSize
    const int LineBudget = 850 * 1024;

    string Root => Path.Combine(dataDir, "logs");

    /// <summary>For tests: runs on the worker with the file id before a file is read.</summary>
    public Action<string>? ReadHook { get; set; }

    /// <summary>Newest first.</summary>
    /// <param name="channelNames">Current channel names; a channel missing there (deleted) is named after its file.</param>
    public List<LogFileInfo> List(IReadOnlyDictionary<Guid, string>? channelNames = null) => ListWithPaths(channelNames).Select(locatedLogFile => locatedLogFile.Info).ToList();

    /// <summary>The listing with each file's path, in the same order; LogExporter copies from it.</summary>
    internal List<LocatedLogFile> ListWithPaths(IReadOnlyDictionary<Guid, string>? channelNames) =>
        Files().Select(logFileLocation =>
            {
                var info = new FileInfo(logFileLocation.Path);
                var name = logFileLocation.ChannelId is { } id && channelNames is not null ? channelNames.GetValueOrDefault(id) ?? NameInFile(logFileLocation.Path) : null;
                return new LocatedLogFile(new LogFileInfo(logFileLocation.Id, logFileLocation.ChannelId is null ? LogKind.Server : LogKind.Channel, logFileLocation.ChannelId, name,
                    StartOf(info), new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero), info.Length), logFileLocation.Path);
            })
            .OrderByDescending(locatedLogFile => locatedLogFile.Info.Start).ThenBy(locatedLogFile => locatedLogFile.Info.Kind).ThenBy(locatedLogFile => locatedLogFile.Info.Id, StringComparer.Ordinal).ToList();

    /// <summary>The file with exactly this id in the listing, null for anything else.</summary>
    public string? Resolve(string? id) => Files().FirstOrDefault(logFileLocation => logFileLocation.Id == id).Path;

    IEnumerable<LogFileLocation> Files()
    {
        foreach (var path in LogsIn(Path.Combine(Root, ServerFolder)))
            yield return new LogFileLocation($"{ServerFolder}/{Path.GetFileName(path)}", path, null);
        var channels = new DirectoryInfo(Path.Combine(Root, ChannelFolder));
        if (!channels.Exists) yield break;
        foreach (var dir in channels.EnumerateDirectories())
        {
            if (dir.Attributes.HasFlag(FileAttributes.ReparsePoint) || !Guid.TryParseExact(dir.Name, "D", out var id) || dir.Name != id.ToString()) continue;
            foreach (var path in LogsIn(dir.FullName))
                yield return new LogFileLocation($"{ChannelFolder}/{dir.Name}/{Path.GetFileName(path)}", path, id);
        }
    }

    static IEnumerable<string> LogsIn(string folder)
    {
        var dir = new DirectoryInfo(folder);
        if (!dir.Exists || dir.Attributes.HasFlag(FileAttributes.ReparsePoint)) return [];
        return dir.EnumerateFiles("*.log")
            .Where(fileInfo => fileInfo.Name.EndsWith(".log", StringComparison.Ordinal) && !fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
            .Select(fileInfo => fileInfo.FullName);
    }

    /// <summary>The start in the name (server local time, like LogFileWriter writes it), else the creation time.</summary>
    DateTimeOffset StartOf(FileInfo file)
    {
        var name = Path.GetFileNameWithoutExtension(file.Name);
        if (name.Length >= LogFileWriter.NameFormat.Length
            && DateTime.TryParseExact(name[^LogFileWriter.NameFormat.Length..], LogFileWriter.NameFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            return new DateTimeOffset(local, time.LocalTimeZone.GetUtcOffset(local));
        return new DateTimeOffset(file.CreationTimeUtc, TimeSpan.Zero);
    }

    /// <summary>A channel line reads "yyyy-MM-dd HH:mm:ss.fff [Name] text"; the first line gives the name.</summary>
    static string? NameInFile(string path)
    {
        var line = ReadLines(path).FirstOrDefault();
        const int open = 24; // after the time stamp and a space
        if (line is null || line.Length <= open || line[open] != '[') return null;
        var close = line.IndexOf("] ", open, StringComparison.Ordinal);
        return close > open + 1 ? line[(open + 1)..close] : null;
    }

    static IEnumerable<string> ReadLines(string path)
    {
        // shared like LogFileWriter appends: the running server keeps writing, retention may delete meanwhile
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (reader.ReadLine() is { } line) yield return line;
    }

    /// <summary>Page of WireProtocol.LogPageLines lines, the last one for null or a page past the end.</summary>
    public LogPage ReadPage(string? requestId, string id, string path, int? page)
    {
        ReadHook?.Invoke(id);
        const int size = WireProtocol.LogPageLines;
        var wanted = new List<string>();
        var last = new List<string>();
        int total = 0;
        foreach (var line in ReadLines(path))
        {
            if (total % size == 0) last.Clear();
            var cut = Cut(line, ServerTuning.MaxLogLineLength);
            last.Add(cut);
            if (page is { } pageNumber && total / size == pageNumber - 1) wanted.Add(cut);
            total++;
        }
        int count = Math.Max(1, (total + size - 1) / size);
        int shown = page is { } limit && limit <= count ? limit : count;
        var lines = shown == count ? last : wanted;
        return new LogPage(requestId, id, shown, count, (shown - 1) * size + 1, Fit(lines));
    }

    /// <summary>
    /// Plain text, case-insensitive, over every file that passes the filters (a period keeps files that overlap it).
    /// Newest file first and from its end; at most MaxLogHits, stops after LogSearchTimeout with what it found.
    /// </summary>
    public LogSearchResult Search(string? requestId, SearchLogs search)
    {
        long started = time.GetTimestamp();
        var hits = new List<LogHit>();
        bool timedOut = false;
        var files = ListWithPaths(null).Where(locatedLogFile => locatedLogFile.Info is var logFile
            && (search.Kind is null || logFile.Kind == search.Kind) && (search.ChannelId is null || logFile.ChannelId == search.ChannelId)
            && (search.To is null || logFile.Start <= search.To) && (search.From is null || logFile.LastWrite >= search.From));
        foreach (var (file, path) in files)
        {
            if (hits.Count > ProtocolLimits.MaxLogHits || timedOut) break;
            ReadHook?.Invoke(file.Id);
            var found = new Queue<LogHit>(); // the file's last hits, as many as can still be used
            int room = ProtocolLimits.MaxLogHits + 1 - hits.Count, number = 0;
            foreach (var line in ReadLines(path))
            {
                number++;
                if (number % 1024 == 0 && time.GetElapsedTime(started) > ServerTuning.LogSearchTimeout)
                {
                    timedOut = true;
                    break;
                }
                if (!line.Contains(search.Query, StringComparison.OrdinalIgnoreCase)) continue;
                found.Enqueue(new LogHit(file.Id, number, Cut(line, ServerTuning.MaxLogLineLength)));
                if (found.Count > room) found.Dequeue();
            }
            hits.AddRange(found.Reverse());
        }
        bool truncated = hits.Count > ProtocolLimits.MaxLogHits;
        if (truncated) hits.RemoveRange(ProtocolLimits.MaxLogHits, hits.Count - ProtocolLimits.MaxLogHits);
        var texts = Fit(hits.Select(logHit => logHit.Text).ToList());
        return new LogSearchResult(requestId, hits.Select((logHit, index) => logHit with { Text = texts[index] }).ToList(), truncated, timedOut);
    }

    /// <summary>At most max characters plus the marker; never splits a surrogate pair.</summary>
    static string Cut(string line, int max)
    {
        if (line.Length <= max) return line;
        if (max > 0 && char.IsHighSurrogate(line[max - 1])) max--;
        return line[..max] + CutMarker;
    }

    static int JsonBytes(string text) => JsonEncodedText.Encode(text).EncodedUtf8Bytes.Length + 3; // quotes and comma

    /// <summary>
    /// Keeps one answer below the frame limit (AC6): when the lines together are too large as JSON (non-ASCII takes
    /// 6 bytes a character), every line is cut to the longest length that still fits.
    /// </summary>
    static List<string> Fit(List<string> lines)
    {
        if (lines.Sum(JsonBytes) <= LineBudget) return lines;
        int low = 0, high = ServerTuning.MaxLogLineLength;
        while (low < high)
        {
            int mid = (low + high + 1) / 2;
            if (lines.Sum(text => JsonBytes(Cut(text, mid))) <= LineBudget) low = mid;
            else high = mid - 1;
        }
        return lines.Select(text => Cut(text, low)).ToList();
    }
}
