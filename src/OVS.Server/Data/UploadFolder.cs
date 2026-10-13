using OVS.Shared.IO;

namespace OVS.Server.Data;

/// <summary>
/// Package 75: uploads land in a hidden file in the backups folder until the whole archive is checked. Knows the upload
/// ids, their paths, and removes what a crash or a lost connection left behind.
/// </summary>
internal sealed class UploadFolder(string folder)
{
    /// <summary>The name prefix of an unfinished upload's file.</summary>
    public const string Prefix = ".upload-";

    /// <summary>32 lowercase hex digits (Guid "N"), so an id can never name a path.</summary>
    public static bool IsValidId(string? id) => id is { Length: 32 } && id.All(char.IsAsciiHexDigitLower);

    /// <summary>The hidden file of an upload, with the folder created. Only call with an id that passed IsValidId.</summary>
    public string PreparePath(string id)
    {
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, Prefix + id);
    }

    /// <summary>Unfinished uploads a crash or a lost connection left behind.</summary>
    public void RemoveLeftovers()
    {
        if (!Directory.Exists(folder)) return;
        foreach (var file in Directory.GetFiles(folder, Prefix + "*")) SafeFile.DeleteQuietly(file);
    }
}
