using System.Text.RegularExpressions;

namespace OVS.Tests.TestSupport;

/// <summary>
/// A docker-compose file read line by line (Packages 149 and M1): the files are plain and small, so no YAML parser.
/// Each service maps to the trimmed lines of its block, comments and blank lines left out.
/// </summary>
internal static partial class ComposeFile
{
    /// <summary>Each service of the compose file <paramref name="path"/> with the lines of its block.</summary>
    public static Dictionary<string, List<string>> ReadServices(string path)
    {
        var services = new Dictionary<string, List<string>>();
        List<string>? current = null;
        bool inServices = false;
        foreach (var line in File.ReadAllLines(path))
        {
            if (line.TrimStart().StartsWith('#') || line.Trim().Length == 0) continue;
            if (!line.StartsWith(' ')) inServices = line == "services:";
            else if (inServices && ServiceLine().Match(line) is { Success: true } service) services[service.Groups[1].Value] = current = [];
            else if (inServices) current?.Add(line.Trim());
        }
        return services;
    }

    /// <summary>The top-level block <paramref name="name"/> (such as <c>networks:</c>) with its trimmed lines.</summary>
    public static List<string> ReadBlock(string path, string name)
    {
        var lines = new List<string>();
        bool inBlock = false;
        foreach (var line in File.ReadAllLines(path))
        {
            if (line.TrimStart().StartsWith('#') || line.Trim().Length == 0) continue;
            if (!line.StartsWith(' ')) inBlock = line == name + ":";
            else if (inBlock) lines.Add(line.Trim());
        }
        return lines;
    }

    /// <summary>True for an image with a fixed version (a numeric tag, also with a leading <c>v</c>), never <c>:latest</c> or a bare major.</summary>
    public static bool IsPinned(string image) => PinnedTag().IsMatch(image);

    [GeneratedRegex(@"^  ([a-z0-9-]+):$")]
    private static partial Regex ServiceLine();

    [GeneratedRegex(@":v?\d+(\.\d+)+$")]
    private static partial Regex PinnedTag();
}
