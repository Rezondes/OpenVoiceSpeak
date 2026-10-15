using OVS.Shared.IO;
using OVS.Shared.Protocol;

namespace OVS.Server.Data;

/// <summary>
/// Package 168: the profile pictures of this server, one file per user in <c>avatars/</c>, each a PNG that passed
/// <see cref="AvatarFormat.Validate"/>. The file name is the user's fingerprint and nothing else (64 lower-case hex
/// characters, A170): no text a client chose ever reaches a path. A file that fails the check on loading counts as none.
/// </summary>
internal sealed class AvatarStore
{
    /// <summary>The folder's name inside the data folder (also inside a backup).</summary>
    public const string FolderName = "avatars";

    readonly string folder;
    readonly Dictionary<string, Picture> byFingerprint = [];

    /// <summary>Loads the stored pictures of <paramref name="dataDir"/>.</summary>
    public AvatarStore(string dataDir)
    {
        folder = Path.Combine(dataDir, FolderName);
        if (!Directory.Exists(folder)) return;
        foreach (var file in Directory.EnumerateFiles(folder, "*.png"))
        {
            var fingerprint = Path.GetFileNameWithoutExtension(file);
            if (!IsFingerprint(fingerprint)) continue;
            var png = File.ReadAllBytes(file);
            if (AvatarFormat.Validate(png) is null) byFingerprint[fingerprint] = new Picture(AvatarHash.Of(png), png);
        }
    }

    /// <summary>A fingerprint as the server makes them: 64 lower-case hex characters.</summary>
    public static bool IsFingerprint(string? text) => text is { Length: 64 } && text.All(char.IsAsciiHexDigitLower);

    /// <summary>The file name of a user's picture, null for anything that is no fingerprint.</summary>
    public static string? FileNameFor(string fingerprint) => IsFingerprint(fingerprint) ? fingerprint + ".png" : null;

    /// <summary>The hash of the user's picture, null when there is none.</summary>
    public AvatarHash? HashOf(string fingerprint) => byFingerprint.TryGetValue(fingerprint, out var picture) ? picture.Hash : null;

    /// <summary>The picture with this hash as Base64, null when no user has it (any more).</summary>
    public string? Find(AvatarHash hash) => byFingerprint.Values.FirstOrDefault(picture => picture.Hash == hash)?.Base64;

    /// <summary>Stores a validated PNG as the user's picture (write, then rename), or removes it with null.</summary>
    /// <exception cref="ArgumentException">The fingerprint is no fingerprint.</exception>
    public void Set(string fingerprint, byte[]? png)
    {
        var path = Path.Combine(folder, FileNameFor(fingerprint) ?? throw new ArgumentException("No fingerprint.", nameof(fingerprint)));
        if (png is null)
        {
            if (File.Exists(path)) File.Delete(path); // no folder yet: nothing to remove
            byFingerprint.Remove(fingerprint);
            return;
        }
        Directory.CreateDirectory(folder);
        SafeFile.WriteAtomic(path, png, flushToDisk: false);
        byFingerprint[fingerprint] = new Picture(AvatarHash.Of(png), png);
    }

    /// <summary>One stored picture; Base64 built once, not per request (as the logo's, Package 86).</summary>
    sealed record Picture(AvatarHash Hash, byte[] Png)
    {
        public string Base64 { get; } = Convert.ToBase64String(Png);
    }
}
