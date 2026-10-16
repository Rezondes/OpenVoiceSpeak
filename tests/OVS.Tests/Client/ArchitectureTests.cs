using System.Text.RegularExpressions;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client;

/// <summary>
/// Package 180 (A180): the client has one native audio library, <c>ovs_audio.dll</c>. Its code names none of the parts
/// the managed path used (Concentus, the RRNoise package, <c>rnnoise.dll</c>, <c>ovs_aec.dll</c>), its project neither,
/// and NAudio's wave types serve only the sound import, which reads WAV and MP3 files (no output, no capture).
/// Comments never count: the patterns are looked for in a line's code.
/// </summary>
public sealed class ArchitectureTests
{
    static readonly Regex Removed = new(@"\bConcentus\b|\bRRNoise\b|\bovs_aec\b|""rnnoise", RegexOptions.IgnoreCase);
    static readonly Regex WaveTypes = new(@"\bNAudio\.Wave\b");
    static readonly string SoundImport = Path.Combine("Audio", "Sounds") + Path.DirectorySeparatorChar;

    [Fact]
    public void Client_NoReference_ToNAudioWaveConcentusRnnoiseOrOvsAec()
    {
        var found = new List<string>();
        foreach (var file in RepoPaths.SourceFiles(RepoPaths.ClientSource, "*.cs"))
        {
            var relative = Path.GetRelativePath(RepoPaths.ClientSource, file);
            var lines = File.ReadAllLines(file);
            for (int index = 0; index < lines.Length; index++)
            {
                var code = lines[index].Split("//")[0];
                if (Removed.IsMatch(code) || (WaveTypes.IsMatch(code) && !relative.StartsWith(SoundImport, StringComparison.Ordinal)))
                    found.Add($"{relative}:{index + 1}: {lines[index].Trim()}");
            }
        }
        if (Removed.IsMatch(File.ReadAllText(Path.Combine(RepoPaths.ClientSource, "OVS.Client.csproj")))) found.Add("OVS.Client.csproj");
        Assert.Empty(found);
    }

    [Fact]
    public void Client_NativeLibraries_OnlyOvsAudio()
    {
        var native = Path.Combine(RepoPaths.ClientSource, "runtimes", "win-x64", "native");
        Assert.Equal(["ovs_audio.dll"], Directory.GetFiles(native).Select(Path.GetFileName));
    }
}
