using System.IO.Compression;
using System.Text.Json;
using OVS.Server.Data;
using OVS.Server.Data.Records;
using OVS.Server.Tls;
using OVS.Shared.Protocol.Framing;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server.Data;

public sealed class BackupArchiveTests() : TempDirectoryTest("ovs-archive-", quiet: false)
{
    const string FixtureName = "2026-10-06_12-00-00.ovsbackup";
    static readonly BackupManifest Manifest = new(BackupArchive.FormatVersion, ServerData.CurrentVersion, "1.2.3", PersistedFixtures.Date);

    static Dictionary<string, byte[]> Entries(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        return zip.Entries.ToDictionary(zipArchiveEntry => zipArchiveEntry.FullName, zipArchiveEntry =>
        {
            using var stream = zipArchiveEntry.Open();
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return copy.ToArray();
        });
    }

    static string[] EntryNames(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        return [.. zip.Entries.Select(zipArchiveEntry => zipArchiveEntry.FullName)];
    }

    static void WriteZip(string path, Dictionary<string, byte[]> entries)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, bytes) in entries)
        {
            using var stream = zip.CreateEntry(name).Open();
            stream.Write(bytes);
        }
    }

    Dictionary<string, byte[]>? validFiles;

    /// <summary>A valid server data file and certificate, taken from the persisted backup fixture.</summary>
    Dictionary<string, byte[]> ValidFiles()
    {
        if (validFiles is null)
        {
            var fixture = Entries(PersistedFixtures.CopyTo(PersistedFixtures.Current, FixtureName, Path.Combine(dir, "fixture")));
            validFiles = new() { [DataStore.FileName] = fixture[DataStore.FileName], [ServerCertificate.FileName] = fixture[ServerCertificate.FileName] };
        }
        return new(validFiles);
    }

    /// <summary>A data folder with the given live files.</summary>
    string LiveFolder(Dictionary<string, byte[]> files)
    {
        var live = Directory.CreateDirectory(Path.Combine(dir, "live")).FullName;
        foreach (var (name, bytes) in files) File.WriteAllBytes(Path.Combine(live, name), bytes);
        return live;
    }

    /// <summary>An archive of the valid files with one entry replaced (or removed with null).</summary>
    string ArchiveWith(string entry, byte[]? bytes)
    {
        var entries = new Dictionary<string, byte[]>(ValidFiles()) { ["manifest.json"] = JsonSerializer.SerializeToUtf8Bytes(Manifest, ProtocolJson.Options) };
        if (bytes is null) entries.Remove(entry);
        else entries[entry] = bytes;
        var path = Path.Combine(dir, "test.ovsbackup");
        WriteZip(path, entries);
        return path;
    }

    static byte[] ManifestJson(object manifest) => JsonSerializer.SerializeToUtf8Bytes(manifest, ProtocolJson.Options);

    [Fact]
    public void Write_LiveFilesWithIcon_EntriesInOrderWithTheirBytes()
    {
        var files = new Dictionary<string, byte[]> { [DataStore.FileName] = [1, 2], [ServerCertificate.FileName] = [3], [ServerIconStore.FileName] = [4, 5, 6] };
        var path = Path.Combine(dir, "neu.ovsbackup");

        BackupArchive.Write(path, Manifest, LiveFolder(files));

        Assert.Equal(["manifest.json", DataStore.FileName, ServerCertificate.FileName, ServerIconStore.FileName], EntryNames(path));
        var entries = Entries(path);
        Assert.Equal(ManifestJson(Manifest), entries["manifest.json"]);
        foreach (var (name, bytes) in files) Assert.Equal(bytes, entries[name]);
    }

    [Fact]
    public void Write_NoIcon_NoIconEntry()
    {
        var path = Path.Combine(dir, "neu.ovsbackup");

        BackupArchive.Write(path, Manifest, LiveFolder(new() { [DataStore.FileName] = [1], [ServerCertificate.FileName] = [2] }));

        Assert.Equal(["manifest.json", DataStore.FileName, ServerCertificate.FileName], EntryNames(path));
    }

    [Fact]
    public void Read_WrittenArchiveOfValidFiles_ContentEqualsTheFiles()
    {
        var files = ValidFiles();
        var path = Path.Combine(dir, "neu.ovsbackup");
        BackupArchive.Write(path, Manifest, LiveFolder(files));

        var content = BackupArchive.Read(path);

        Assert.Equal(Manifest, content.Manifest);
        Assert.Equal(files[DataStore.FileName], content.Data);
        Assert.Equal(files[ServerCertificate.FileName], content.Certificate);
        Assert.Null(content.Icon);
    }

    [Fact]
    public void Read_NotAZip_InvalidData()
    {
        var path = Path.Combine(dir, "kaputt.ovsbackup");
        File.WriteAllBytes(path, [1, 2, 3, 4]);

        Assert.Throws<InvalidDataException>(() => BackupArchive.Read(path));
    }

    public static TheoryData<string, byte[]?, string> Broken => new()
    {
        { "manifest.json", null, "manifest.json is missing" },
        { "manifest.json", ManifestJson(new { formatVersion = 1, dataVersion = 1, createdAt = PersistedFixtures.Date }), "manifest.json without a server version" },
        { "manifest.json", ManifestJson(Manifest with { FormatVersion = 0 }), "unknown format 0" },
        { "manifest.json", ManifestJson(Manifest with { FormatVersion = 2 }), "unknown format 2" },
        { "manifest.json", ManifestJson(Manifest with { DataVersion = ServerData.CurrentVersion + 1 }), $"data version {ServerData.CurrentVersion + 1} is newer than {ServerData.CurrentVersion}" },
        { "manifest.json", new byte[16 * 1024 + 1], "manifest.json is too large" },
        { DataStore.FileName, null, $"{DataStore.FileName} is missing" },
        { DataStore.FileName, "null"u8.ToArray(), $"{DataStore.FileName} is empty" },
        { ServerCertificate.FileName, null, $"{ServerCertificate.FileName} is missing" },
        { ServerIconStore.FileName, [1, 2, 3], "logo invalid" },
    };

    [Theory]
    [MemberData(nameof(Broken))]
    public void Read_BrokenEntry_InvalidDataWithTheReason(string entry, byte[]? bytes, string reason)
    {
        var path = ArchiveWith(entry, bytes);

        Assert.StartsWith(reason, Assert.Throws<InvalidDataException>(() => BackupArchive.Read(path)).Message);
    }

    [Fact]
    public void Read_ArchiveWithValidLogo_ContentHasTheLogo()
    {
        var logo = TestImages.Encode(128, 128);
        var path = ArchiveWith(ServerIconStore.FileName, logo);

        Assert.Equal(logo, BackupArchive.Read(path).Icon);
    }

    [Fact]
    public void Read_ManifestOfExactlyTheCap_Accepted()
    {
        var json = ManifestJson(Manifest);
        var padded = json.Concat(Enumerable.Repeat((byte)' ', 16 * 1024 - json.Length)).ToArray();
        var path = ArchiveWith("manifest.json", padded);

        Assert.Equal(Manifest, BackupArchive.Read(path).Manifest);
    }

    [Fact]
    public void Read_DataNewerThanTheServer_InvalidData()
    {
        var data = JsonSerializer.Deserialize<ServerData>(ValidFiles()[DataStore.FileName], ProtocolJson.Options)!;
        data.DataVersion = ServerData.CurrentVersion + 1;
        var path = ArchiveWith(DataStore.FileName, JsonSerializer.SerializeToUtf8Bytes(data, ProtocolJson.Options));

        Assert.Equal($"data version {data.DataVersion} is newer than {ServerData.CurrentVersion}", Assert.Throws<InvalidDataException>(() => BackupArchive.Read(path)).Message);
    }

    [Fact]
    public void Read_DataWithAProblem_InvalidDataWithTheProblem()
    {
        var data = JsonSerializer.Deserialize<ServerData>(ValidFiles()[DataStore.FileName], ProtocolJson.Options)!;
        BrokenServerData.Break(data, "no-admin");
        var path = ArchiveWith(DataStore.FileName, JsonSerializer.SerializeToUtf8Bytes(data, ProtocolJson.Options));

        Assert.Equal(data.FindProblem(), Assert.Throws<InvalidDataException>(() => BackupArchive.Read(path)).Message);
    }

    [Fact]
    public void Read_UnreadableCertificate_InvalidData()
    {
        var path = ArchiveWith(ServerCertificate.FileName, [1, 2, 3]);

        Assert.Throws<InvalidDataException>(() => BackupArchive.Read(path));
    }

    [Fact]
    public void TryReadManifest_ValidArchive_TheManifest()
    {
        var path = Path.Combine(dir, "neu.ovsbackup");
        BackupArchive.Write(path, Manifest, LiveFolder(new() { [DataStore.FileName] = [1], [ServerCertificate.FileName] = [2] }));

        Assert.Equal(Manifest, BackupArchive.TryReadManifest(path));
    }

    [Fact]
    public void TryReadManifest_NotAZip_Null()
    {
        var path = Path.Combine(dir, "kaputt.ovsbackup");
        File.WriteAllBytes(path, [1, 2, 3]);

        Assert.Null(BackupArchive.TryReadManifest(path));
    }

    [Fact]
    public void TryReadManifest_NoManifest_Null()
    {
        var path = ArchiveWith("manifest.json", null);

        Assert.Null(BackupArchive.TryReadManifest(path));
    }

    [Fact]
    public void Validate_ValidData_Accepted()
    {
        var data = JsonSerializer.Deserialize<ServerData>(ValidFiles()[DataStore.FileName], ProtocolJson.Options)!;

        BackupArchive.Validate(data);
    }

    [Fact]
    public void Validate_DataWithAProblem_InvalidDataWithTheProblem()
    {
        var data = JsonSerializer.Deserialize<ServerData>(ValidFiles()[DataStore.FileName], ProtocolJson.Options)!;
        BrokenServerData.Break(data, "no-default-channel");

        Assert.Equal(data.FindProblem(), Assert.Throws<InvalidDataException>(() => BackupArchive.Validate(data)).Message);
    }
}
