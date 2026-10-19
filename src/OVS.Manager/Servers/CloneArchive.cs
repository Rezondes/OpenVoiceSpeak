using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using OVS.Shared.Permissions;

namespace OVS.Manager.Servers;

/// <summary>
/// Package M30 (AC1, AC2): the archive a clone restores. It holds the source's data (channels, links, groups, settings),
/// logo and, when chosen, its users with their profile pictures and its bans, under the clone's name and with the clone's
/// own certificate. The clone's users (the manager's identity) join, so the manager stays its only server admin.
/// </summary>
internal static class CloneArchive
{
    // the entry names of the server's archive (OVS.Server BackupArchive, Package 74)
    const string ManifestEntry = "manifest.json";
    const string DataEntry = "server-data.json";
    const string CertificateEntry = "cert.pfx";
    const string IconEntry = "server-icon.png";
    const string AvatarFolder = "avatars/";

    /// <summary>The archive of <paramref name="source"/>'s contents for the clone of <paramref name="clone"/> named <paramref name="name"/>.</summary>
    /// <exception cref="InvalidDataException">An archive misses its manifest, data or certificate.</exception>
    public static byte[] Merge(byte[] source, byte[] clone, string name, bool keepUsers, bool keepBans)
    {
        using var sourceZip = new ZipArchive(new MemoryStream(source), ZipArchiveMode.Read);
        using var cloneZip = new ZipArchive(new MemoryStream(clone), ZipArchiveMode.Read);
        var data = JsonNode.Parse(Required(sourceZip, DataEntry))!.AsObject();
        var own = JsonNode.Parse(Required(cloneZip, DataEntry))!.AsObject();
        data["settings"]!["name"] = name;
        data["users"] = Users(keepUsers ? data["users"]!.AsArray() : [], own["users"]!.AsArray());
        if (!keepBans) data["bans"] = new JsonArray();

        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, ManifestEntry, Required(sourceZip, ManifestEntry));
            Add(zip, DataEntry, JsonSerializer.SerializeToUtf8Bytes(data));
            Add(zip, CertificateEntry, Required(cloneZip, CertificateEntry));
            if (sourceZip.GetEntry(IconEntry) is { } icon) Add(zip, IconEntry, Read(icon));
            if (keepUsers)
                foreach (var avatar in sourceZip.Entries.Where(entry => entry.FullName.StartsWith(AvatarFolder, StringComparison.Ordinal)))
                    Add(zip, avatar.FullName, Read(avatar));
        }
        return output.ToArray();
    }

    /// <summary>The source's kept users without the Server-Admin group, then the clone's own users (A191: one server admin).</summary>
    static JsonArray Users(JsonArray kept, JsonArray own)
    {
        var users = new JsonArray();
        foreach (var user in kept)
        {
            var copy = user!.DeepClone();
            var groups = copy["groupIds"]!.AsArray();
            foreach (var group in groups.Where(group => group!.GetValue<Guid>() == WellKnownGroups.ServerAdmin).ToList()) groups.Remove(group);
            users.Add(copy);
        }
        var fingerprints = users.Select(user => user!["fingerprint"]!.GetValue<string>()).ToHashSet();
        foreach (var user in own.Where(user => !fingerprints.Contains(user!["fingerprint"]!.GetValue<string>()))) users.Add(user!.DeepClone());
        return users;
    }

    static byte[] Required(ZipArchive zip, string name) =>
        zip.GetEntry(name) is { } entry ? Read(entry) : throw new InvalidDataException($"{name} is missing");

    static byte[] Read(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    static void Add(ZipArchive zip, string name, byte[] bytes)
    {
        using var stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        stream.Write(bytes);
    }
}
