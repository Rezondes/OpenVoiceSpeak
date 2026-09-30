using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using OVS.Server.Data;
using OVS.Shared.Protocol;

namespace OVS.Server.Logging;

/// <summary>
/// Package 81 (A98): lists, pages and searches the log files ServerLogs writes. Only *.log files directly in
/// logs/server and logs/channels/&lt;guid&gt; exist for it, never links; a file is found only by the id of its own listing,
/// so no path from a client is ever opened. Thread-safe, meant to run off the state lock.
/// </summary>
public sealed class LogReader(string dataDir, TimeProvider time)
{
    const string NameFormat = "yyyy-MM-dd_HH-mm-ss";
    const string ServerFolder = "server";
    const string ChannelFolder = "channels";
    public const string CutMarker = " [...]";
    // JSON bytes the lines of one answer may take; the rest (ids, numbers) stays well below FrameReader.MaxFrameSize
    const int LineBudget = 850 * 1024;

    string Root => Path.Combine(dataDir, "logs");

    /// <summary>For tests: runs on the worker with the file id before a file is read.</summary>
    public Action<string>? ReadHook { get; set; }

    /// <summary>Newest first.</summary>
    /// <param name="channelNames">Current channel names; a channel missing there (deleted) is named after its file.</param>
    public List<LogFileInfo> List(IReadOnlyDictionary<Guid, string>? channelNames = null) => Entries(channelNames).Select(e => e.Info).ToList();

    List<(LogFileInfo Info, string Path)> Entries(IReadOnlyDictionary<Guid, string>? channelNames) =>
        Files().Select(f =>
            {
                var info = new FileInfo(f.Path);
                var name = f.ChannelId is { } id && channelNames is not null ? channelNames.GetValueOrDefault(id) ?? NameInFile(f.Path) : null;
                return (new LogFileInfo(f.Id, f.ChannelId is null ? LogKind.Server : LogKind.Channel, f.ChannelId, name,
                    StartOf(info), new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero), info.Length), f.Path);
            })
            .OrderByDescending(e => e.Item1.Start).ThenBy(e => e.Item1.Kind).ThenBy(e => e.Item1.Id, StringComparer.Ordinal).ToList();

    /// <summary>The file with exactly this id in the listing, null for anything else.</summary>
    public string? Resolve(string? id) => Files().FirstOrDefault(f => f.Id == id).Path;

    IEnumerable<(string Id, string Path, Guid? ChannelId)> Files()
    {
        foreach (var path in LogsIn(Path.Combine(Root, ServerFolder)))
            yield return ($"{ServerFolder}/{Path.GetFileName(path)}", path, null);
        var channels = new DirectoryInfo(Path.Combine(Root, ChannelFolder));
        if (!channels.Exists) yield break;
        foreach (var dir in channels.EnumerateDirectories())
        {
            if (dir.Attributes.HasFlag(FileAttributes.ReparsePoint) || !Guid.TryParseExact(dir.Name, "D", out var id) || dir.Name != id.ToString()) continue;
            foreach (var path in LogsIn(dir.FullName))
                yield return ($"{ChannelFolder}/{dir.Name}/{Path.GetFileName(path)}", path, id);
        }
    }

    static IEnumerable<string> LogsIn(string folder)
    {
        var dir = new DirectoryInfo(folder);
        if (!dir.Exists || dir.Attributes.HasFlag(FileAttributes.ReparsePoint)) return [];
        return dir.EnumerateFiles("*.log")
            .Where(f => f.Name.EndsWith(".log", StringComparison.Ordinal) && !f.Attributes.HasFlag(FileAttributes.ReparsePoint))
            .Select(f => f.FullName);
    }

    /// <summary>The start in the name (server local time, like LogFiles writes it), else the creation time.</summary>
    DateTimeOffset StartOf(FileInfo file)
    {
        var name = Path.GetFileNameWithoutExtension(file.Name);
        if (name.Length >= NameFormat.Length
            && DateTime.TryParseExact(name[^NameFormat.Length..], NameFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
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
        // shared like LogFiles appends: the running server keeps writing, retention may delete meanwhile
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (reader.ReadLine() is { } line) yield return line;
    }

    /// <summary>Page of ProtocolInfo.LogPageLines lines, the last one for null or a page past the end.</summary>
    public LogPage ReadPage(string? requestId, string id, string path, int? page)
    {
        ReadHook?.Invoke(id);
        const int size = ProtocolInfo.LogPageLines;
        var wanted = new List<string>();
        var last = new List<string>();
        int total = 0;
        foreach (var line in ReadLines(path))
        {
            if (total % size == 0) last.Clear();
            var cut = Cut(line, ProtocolInfo.MaxLogLineLength);
            last.Add(cut);
            if (page is { } p && total / size == p - 1) wanted.Add(cut);
            total++;
        }
        int count = Math.Max(1, (total + size - 1) / size);
        int shown = page is { } n && n <= count ? n : count;
        var lines = shown == count ? last : wanted;
        return new LogPage(requestId, id, shown, count, (shown - 1) * size + 1, Fit(lines));
    }

    /// <summary>
    /// Plain text, case-insensitive, over every file that passes the filters (a period keeps files that overlap it).
    /// Newest file first and from its end; at most MaxLogHits, stops after LogSearchTimeout with what it found.
    /// </summary>
    public LogSearchResult Search(string? requestId, SearchLogs search)
    {
        var clock = Stopwatch.StartNew();
        var hits = new List<LogHit>();
        bool timedOut = false;
        var files = Entries(null).Where(e => e.Info is var f
            && (search.Kind is null || f.Kind == search.Kind) && (search.ChannelId is null || f.ChannelId == search.ChannelId)
            && (search.To is null || f.Start <= search.To) && (search.From is null || f.LastWrite >= search.From));
        foreach (var (file, path) in files)
        {
            if (hits.Count > ProtocolInfo.MaxLogHits || timedOut) break;
            ReadHook?.Invoke(file.Id);
            var found = new Queue<LogHit>(); // the file's last hits, as many as can still be used
            int room = ProtocolInfo.MaxLogHits + 1 - hits.Count, number = 0;
            foreach (var line in ReadLines(path))
            {
                number++;
                if (number % 1024 == 0 && clock.Elapsed > ProtocolInfo.LogSearchTimeout)
                {
                    timedOut = true;
                    break;
                }
                if (!line.Contains(search.Query, StringComparison.OrdinalIgnoreCase)) continue;
                found.Enqueue(new LogHit(file.Id, number, Cut(line, ProtocolInfo.MaxLogLineLength)));
                if (found.Count > room) found.Dequeue();
            }
            hits.AddRange(found.Reverse());
        }
        bool truncated = hits.Count > ProtocolInfo.MaxLogHits;
        if (truncated) hits.RemoveRange(ProtocolInfo.MaxLogHits, hits.Count - ProtocolInfo.MaxLogHits);
        var texts = Fit(hits.Select(h => h.Text).ToList());
        return new LogSearchResult(requestId, hits.Select((h, i) => h with { Text = texts[i] }).ToList(), truncated, timedOut);
    }

    // ---- Package 82 (A99): download snapshots in <DataDir>/logs-export, named only by the server's own ids ----

    public const string ExportFolder = "logs-export";
    string ExportDir => Path.Combine(dataDir, ExportFolder);

    /// <param name="FileName">The name the client saves it under.</param>
    public sealed record LogExport(string FileName, string Path, long Size);

    /// <summary>Every export there is; called at start, when no session can own one yet.</summary>
    public void RemoveExports()
    {
        if (!Directory.Exists(ExportDir)) return;
        foreach (var file in Directory.GetFiles(ExportDir)) BackupStore.DeleteQuietly(file);
    }

    /// <summary>
    /// Copies the chosen files as they are now (later lines are not included) into &lt;downloadId&gt;.log for one file, or
    /// one zip with server/... and channels/&lt;name&gt;_&lt;id&gt;/... built from the listing's own names. Null with an error code
    /// for an id not in the listing or more than MaxLogDownloadBytes. IO errors throw and leave no file.
    /// </summary>
    /// <param name="downloadId">Chosen by the server (Guid "N"), never by a client.</param>
    public (LogExport? Export, string Code) Export(string downloadId, IReadOnlyList<string> ids, IReadOnlyDictionary<Guid, string> channelNames)
    {
        var listed = Entries(channelNames).ToDictionary(e => e.Info.Id);
        var chosen = new List<(LogFileInfo Info, string Path)>();
        foreach (var id in ids)
        {
            if (!listed.TryGetValue(id, out var entry)) return (null, Codes.NotFound);
            chosen.Add(entry);
        }
        if (chosen.Sum(e => e.Info.Size) > ProtocolInfo.MaxLogDownloadBytes) return (null, Codes.LogsTooLarge);
        Directory.CreateDirectory(ExportDir);
        bool single = chosen.Count == 1;
        var path = Path.Combine(ExportDir, downloadId + (single ? ".log" : ".zip"));
        try
        {
            if (single)
            {
                ReadHook?.Invoke(chosen[0].Info.Id);
                using var target = File.Create(path);
                CopySnapshot(chosen[0].Path, target);
            }
            else
            {
                using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
                foreach (var (info, source) in chosen)
                {
                    ReadHook?.Invoke(info.Id);
                    using var entry = zip.CreateEntry(EntryName(info, source), CompressionLevel.Optimal).Open();
                    CopySnapshot(source, entry);
                }
            }
        }
        catch
        {
            BackupStore.DeleteQuietly(path);
            throw;
        }
        var size = new FileInfo(path).Length;
        if (size > ProtocolInfo.MaxLogDownloadBytes)
        {
            BackupStore.DeleteQuietly(path);
            return (null, Codes.LogsTooLarge);
        }
        var name = single ? Path.GetFileName(chosen[0].Path)
            : $"ovs-logs_{Local(chosen.Min(e => e.Info.Start)):yyyy-MM-dd}_{Local(chosen.Max(e => e.Info.LastWrite)):yyyy-MM-dd}.zip";
        return (new LogExport(name, path, size), "");
    }

    DateTimeOffset Local(DateTimeOffset at) => TimeZoneInfo.ConvertTime(at, time.LocalTimeZone);

    static string EntryName(LogFileInfo info, string source) => info.Kind == LogKind.Server
        ? $"{ServerFolder}/{Path.GetFileName(source)}"
        : $"{ChannelFolder}/{SafeName(info.ChannelName)}_{info.ChannelId}/{Path.GetFileName(source)}";

    /// <summary>A channel name as one folder name on every system: no separators or reserved characters, no leading or trailing dots.</summary>
    static string SafeName(string? name)
    {
        var safe = new string((name ?? "").Select(c => char.IsControl(c) || "\\/:*?\"<>|".Contains(c) ? '_' : c).ToArray()).Trim(' ', '.');
        return safe.Length == 0 ? "channel" : safe;
    }

    /// <summary>Exactly the bytes the file has when it is opened; what the server appends meanwhile stays out.</summary>
    static void CopySnapshot(string source, Stream target)
    {
        using var stream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var buffer = new byte[81920];
        for (long left = stream.Length; left > 0;)
        {
            int read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
            if (read == 0) break;
            target.Write(buffer, 0, read);
            left -= read;
        }
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
        int low = 0, high = ProtocolInfo.MaxLogLineLength;
        while (low < high)
        {
            int mid = (low + high + 1) / 2;
            if (lines.Sum(l => JsonBytes(Cut(l, mid))) <= LineBudget) low = mid;
            else high = mid - 1;
        }
        return lines.Select(l => Cut(l, low)).ToList();
    }
}
