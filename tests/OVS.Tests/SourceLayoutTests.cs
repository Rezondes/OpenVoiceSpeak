using OVS.Tests.TestSupport;

namespace OVS.Tests;

/// <summary>
/// The layout rules of CODING-RULES.md §Rule checker on the repository, and the scanner's own tests on small sample
/// trees in a temporary folder: each rule once broken, and the allowed forms the code base uses.
/// </summary>
public sealed class SourceLayoutTests() : TempDirectoryTest("ovs-layout-", quiet: false)
{
    [Fact]
    public void Repository_FollowsLayoutRules()
    {
        var problems = SourceLayout.Check(RepoPaths.Root);
        Assert.True(problems.Count == 0, $"{problems.Count} layout rule violations:\n" + string.Join("\n", problems));
    }

    [Fact]
    public void Scanner_FindsSecondTopLevelType()
    {
        Write("src/OVS.Shared/Voice/Pair.cs", "namespace OVS.Shared.Voice;\n\npublic sealed class Pair;\n\npublic sealed class Other\n{\n}\n");
        Assert.Contains(Check(), p => p.StartsWith("src/OVS.Shared/Voice/Pair.cs: one type per file"));
    }

    [Fact]
    public void Scanner_FindsFileNameMismatch()
    {
        Write("src/OVS.Shared/Voice/Packet.cs", "namespace OVS.Shared.Voice;\n\npublic sealed class VoicePacket\n{\n}\n");
        Write("src/OVS.Shared/Voice/Packet.Extra.cs", "namespace OVS.Shared.Voice;\n\npublic partial class Packet\n{\n}\n");
        var problems = Check();
        Assert.Contains(problems, p => p.StartsWith("src/OVS.Shared/Voice/Packet.cs: file name = type name"));
        Assert.Contains(problems, p => p.StartsWith("src/OVS.Shared/Voice/Packet.Extra.cs: file name = type name"));
    }

    [Fact]
    public void Scanner_FindsNamespaceFolderMismatch()
    {
        Write("src/OVS.Server/Data/Records/UserRecord.cs", "namespace OVS.Server.Data\n{\n    public sealed record UserRecord(string Fingerprint);\n}\n");
        Write("src/OVS.Server/Limits.cs", "public static class Limits\n{\n}\n");
        var problems = Check();
        Assert.Contains(problems, p => p.StartsWith("src/OVS.Server/Data/Records/UserRecord.cs: namespace = folder: OVS.Server.Data"));
        Assert.Contains(problems, p => p.StartsWith("src/OVS.Server/Limits.cs: namespace = folder: no namespace"));
    }

    [Fact]
    public void Scanner_FindsNonPrivateNestedType()
    {
        Write("src/OVS.Client/Audio/Playback/Mixer.cs",
            "namespace OVS.Client.Audio.Playback;\n\npublic sealed class Mixer\n{\n    sealed class Speaker;\n\n    private enum Mode { A, B }\n\n    internal sealed record Slot(int Index);\n}\n");
        var problems = Check();
        Assert.Equal(["src/OVS.Client/Audio/Playback/Mixer.cs: nested types only private: Slot is internal"], problems);
    }

    [Fact]
    public void Scanner_FindsMissingViewSuffix()
    {
        Write("src/OVS.Client/Views/Admin.axaml", "<UserControl xmlns=\"https://github.com/avaloniaui\" x:Class=\"OVS.Client.Views.Admin\" />\n");
        Write("src/OVS.Client/Views/Admin.axaml.cs", "namespace OVS.Client.Views;\n\npublic partial class Admin : UserControl\n{\n}\n");
        var problems = Check();
        Assert.Contains("src/OVS.Client/Views/Admin.axaml: views end in View: Admin", problems);
        Assert.Contains("src/OVS.Client/Views/Admin.axaml.cs: views end in View: Admin", problems);
    }

    [Fact]
    public void Scanner_FindsMissingViewModelSuffix()
    {
        Write("src/OVS.Client/ViewModels/ChatTab.cs", "namespace OVS.Client.ViewModels;\n\npublic sealed class ChatTab\n{\n}\n");
        Write("tests/OVS.Tests/Client/ViewModels/ChatViewModelTests.cs", "namespace OVS.Tests.Client.ViewModels;\n\npublic sealed class ChatViewModelTests\n{\n}\n");
        Assert.Equal(["src/OVS.Client/ViewModels/ChatTab.cs: view models end in ViewModel: ChatTab"], Check());
    }

    [Fact]
    public void Scanner_FindsIgnoredFolderName()
    {
        Write("src/OVS.Server/Logs/LogReader.cs", "namespace OVS.Server.Logs;\n\npublic static class LogReader\n{\n}\n");
        Write("src/OVS.Server/Backups/BackupStore.cs", "namespace OVS.Server.Backups;\n\npublic sealed class BackupStore\n{\n}\n");
        var problems = Check();
        Assert.Contains("src/OVS.Server/Logs/LogReader.cs: no ignored folder name: Logs", problems);
        Assert.Contains("src/OVS.Server/Backups/BackupStore.cs: no ignored folder name: Backups", problems);
    }

    [Fact]
    public void Scanner_FindsNamespaceNamedLikeType()
    {
        Write("src/OVS.Server/ServerSettings/Limits.cs", "namespace OVS.Server.ServerSettings;\n\npublic static class Limits\n{\n}\n");
        Write("src/OVS.Server/Data/Records/ServerSettings.cs", "namespace OVS.Server.Data.Records;\n\npublic sealed class ServerSettings\n{\n}\n");
        Assert.Equal(["src/OVS.Server/ServerSettings/Limits.cs: no namespace segment named like a type: ServerSettings"], Check());
    }

    [Fact]
    public void Scanner_AllowsServerStateParts()
    {
        Write("src/OVS.Server/State/ServerState.cs", "namespace OVS.Server.State;\n\npublic sealed partial class ServerState\n{\n}\n");
        Write("src/OVS.Server/State/ServerState.Admin.cs", "namespace OVS.Server.State;\n\npublic sealed partial class ServerState\n{\n    void OnAdmin() { }\n}\n");
        Assert.Empty(Check());

        Write("src/OVS.Server/ServerState.Chat.cs", "namespace OVS.Server;\n\npublic sealed partial class ServerState\n{\n}\n");
        Assert.Contains(Check(), p => p.StartsWith("src/OVS.Server/ServerState.Chat.cs: file name = type name"));
    }

    [Fact]
    public void Scanner_AllowsGenericFileName()
    {
        Write("src/OVS.Client/Models/Choice.cs", "namespace OVS.Client.Models;\n\npublic sealed record Choice<T>(T Value, string Label)\n    where T : class, new();\n");
        Write("src/OVS.Client/Collections/ListPages.cs",
            "namespace OVS.Client.Collections;\n\npublic sealed class ListPages<T> where T : struct\n{\n    public void Add<TItem>(TItem item) where TItem : class { }\n}\n");
        Assert.Empty(Check());
    }

    [Fact]
    public void Scanner_AllowsMainWindowAndAxamlCodeBehind()
    {
        Write("src/OVS.Client/Views/MainWindow.axaml", "<Window xmlns=\"https://github.com/avaloniaui\" x:Class=\"OVS.Client.Views.MainWindow\" />\n");
        Write("src/OVS.Client/Views/MainWindow.axaml.cs", "namespace OVS.Client.Views;\n\npublic partial class MainWindow : Window\n{\n}\n");
        Write("src/OVS.Client/Controls/BusySpinner.axaml", "<UserControl xmlns=\"https://github.com/avaloniaui\" x:Class=\"OVS.Client.Controls.BusySpinner\" />\n");
        Write("src/OVS.Client/Controls/BusySpinner.axaml.cs", "namespace OVS.Client.Controls;\n\npublic partial class BusySpinner : UserControl\n{\n}\n");
        Write("src/OVS.Client/Styles/Motion.axaml", "<Styles xmlns=\"https://github.com/avaloniaui\" />\n");
        Assert.Empty(Check());
    }

    [Fact]
    public void Scanner_AllowsTopLevelStatements()
    {
        Write("src/OVS.Server/Program.cs",
            "using OVS.Server;\n\nif (args is [\"--version\"])\n{\n    return 0;\n}\n\nusing var stop = new CancellationTokenSource();\nvar record = args.Length;\nforeach (var arg in args) { Console.WriteLine(arg); }\nreturn await ServerHost.RunAsync(c => { c.Cancel = true; });\n");
        Write("tools/render-icon/Program.cs", "using OVS.Tools;\n\nLogoRenderer.Render(args[0]);\n");
        Write("tools/render-icon/LogoRenderer.cs", "namespace OVS.Tools;\n\n[System.Diagnostics.DebuggerDisplay(\"{Size}\")]\npublic static class LogoRenderer\n{\n}\n");
        Assert.Empty(Check());
    }

    [Fact]
    public void Scanner_AllowsRecordsAndAttributes()
    {
        Write("src/OVS.Shared/Protocol/Messages/Users/UserInfo.cs",
            "using System.Text.Json.Serialization;\n\nnamespace OVS.Shared.Protocol.Messages.Users;\n\n/// <summary>A user (class Other {}).</summary>\n[JsonConverter(typeof(Foo))]\n[Obsolete(\"use class Other\")]\npublic sealed record UserInfo(\n    [property: JsonPropertyName(\"id\")] int Id,\n    string Nickname = \"\") : Base(Id)\n{\n    public int[] Values { get; init; } = [1, 2];\n    public static UserInfo Empty { get; } = new(0) { Values = [] };\n}\n");
        Write("src/OVS.Client/Audio/Processing/EchoCanceller.cs",
            "namespace OVS.Client.Audio.Processing;\n\npublic sealed unsafe class EchoCanceller\n{\n    struct Native\n    {\n        public delegate* unmanaged[Cdecl]<nint> Create;\n    }\n\n    readonly Action done = delegate { };\n}\n");
        Write("tests/OVS.Tests/TestSupport/UiTestApp.cs",
            "using OVS.Tests.TestSupport;\n\n[assembly: AvaloniaTestApplication(typeof(UiTestApp))]\n\nnamespace OVS.Tests.TestSupport;\n\npublic static class UiTestApp\n{\n}\n");
        Write("src/OVS.Client/Audio/AudioEngine.cs",
            "namespace OVS.Client.Audio\n{\n    [StructLayout(LayoutKind.Sequential)]\n    public readonly record struct AudioEngine(int Rate)\n    {\n        private readonly record struct Endpoint(string Id);\n    }\n}\n");
        Assert.Empty(Check());
    }

    [Fact]
    public void Scanner_IgnoresBracesInStringsAndComments()
    {
        Write("src/OVS.Shared/Protocol/TextRules.cs",
            "namespace OVS.Shared.Protocol;\n\n// } a stray brace in a comment\npublic static class TextRules\n{\n" +
            "    const string Open = \"{\";\n    const char Close = '}';\n    const string Verbatim = @\"}\"\"class Fake {\";\n" +
            "    const string Quoted = @\"\"\"a}\";\n" +
            "    static string Hole(int x) => $\"{{{x switch { 1 => \"}\", _ => $\"{x}\" }}}\";\n" +
            "    const string Raw = \"\"\"\n        { \"class\": \"Fake\" }\n        \"\"\";\n" +
            "    static string RawHole(int i) => $$\"\"\"{\"id\":\"t{{i}}\"}\"\"\";\n" +
            "    /* } class Hidden { */\n#region Braces { class Hidden\n#endregion\n}\n");
        Assert.Empty(Check());
    }

    List<string> Check() => SourceLayout.Check(dir);

    void Write(string path, string text)
    {
        var full = Path.Combine(dir, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }
}
