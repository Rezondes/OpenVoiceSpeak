using System.IO.Compression;
using OVS.Shared.IO;
using OVS.Shared.Logging;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages.LogAccess;

namespace OVS.Server.Logging;

/// <summary>
/// Package 82 (A99): download snapshots in &lt;DataDir&gt;/logs-export, named only by the server's own ids. The files come
/// from the reader's own listing, so no path from a client is ever opened; the exporter's own ReadHook runs before each copy.
/// Thread-safe, meant to run off the state lock.
/// </summary>
public sealed class LogExporter(string dataDir, LogReader reader, TimeProvider time)
{
    public const string ExportFolder = "logs-export";
    string ExportDir => Path.Combine(dataDir, ExportFolder);

    /// <summary>For tests: runs on the worker with the file id before a file is copied.</summary>
    public Action<string>? ReadHook { get; set; }

    /// <summary>Every export there is; called at start, when no session can own one yet.</summary>
    public void RemoveExports()
    {
        if (!Directory.Exists(ExportDir)) return;
        foreach (var file in Directory.GetFiles(ExportDir)) SafeFile.DeleteQuietly(file);
    }

    /// <summary>
    /// Copies the chosen files as they are now (later lines are not included) into &lt;downloadId&gt;.log for one file, or
    /// one zip with server/... and channels/&lt;name&gt;_&lt;id&gt;/... built from the listing's own names. Null with an error code
    /// for an id not in the listing or more than MaxLogDownloadBytes. IO errors throw and leave no file.
    /// </summary>
    /// <param name="downloadId">Chosen by the server (Guid "N"), never by a client.</param>
    public LogExportResult Export(string downloadId, IReadOnlyList<string> ids, IReadOnlyDictionary<Guid, string> channelNames)
    {
        var listed = reader.ListWithPaths(channelNames).ToDictionary(e => e.Info.Id);
        var chosen = new List<LocatedLogFile>();
        foreach (var id in ids)
        {
            if (!listed.TryGetValue(id, out var entry)) return new LogExportResult(null, ReasonCodes.NotFound);
            chosen.Add(entry);
        }
        if (chosen.Sum(e => e.Info.Size) > ServerTuning.MaxLogDownloadBytes) return new LogExportResult(null, ReasonCodes.LogsTooLarge);
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
            SafeFile.DeleteQuietly(path);
            throw;
        }
        var size = new FileInfo(path).Length;
        if (size > ServerTuning.MaxLogDownloadBytes)
        {
            SafeFile.DeleteQuietly(path);
            return new LogExportResult(null, ReasonCodes.LogsTooLarge);
        }
        var name = single ? Path.GetFileName(chosen[0].Path)
            : $"ovs-logs_{Local(chosen.Min(e => e.Info.Start)):yyyy-MM-dd}_{Local(chosen.Max(e => e.Info.LastWrite)):yyyy-MM-dd}.zip";
        return new LogExportResult(new LogExport(name, path, size), "");
    }

    DateTimeOffset Local(DateTimeOffset at) => TimeZoneInfo.ConvertTime(at, time.LocalTimeZone);

    static string EntryName(LogFileInfo info, string source) => info.Kind == LogKind.Server
        ? $"{LogReader.ServerFolder}/{Path.GetFileName(source)}"
        : $"{LogReader.ChannelFolder}/{SafeName(info.ChannelName)}_{info.ChannelId}/{Path.GetFileName(source)}";

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
}
