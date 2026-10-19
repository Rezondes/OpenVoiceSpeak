using System.Text.RegularExpressions;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Manager;

/// <summary>Package M34: README-MANAGER.md covers the manager, names exactly its variables and only files that exist, and README.md points to it.</summary>
public sealed partial class ManagerDocsTests
{
    static readonly string ReadmePath = Path.Combine(RepoPaths.Root, "README-MANAGER.md");

    static string Readme() => File.ReadAllText(ReadmePath);

    /// <summary>AC1: a heading per topic.</summary>
    [Fact]
    public void Readme_Headings_CoverEveryTopic()
    {
        var headings = File.ReadAllLines(ReadmePath).Where(line => line.StartsWith("## ", StringComparison.Ordinal)).Select(line => line[3..]).ToList();
        Assert.Equal(
        [
            "Was das Verwaltungstool ist", "Einrichtung mit Docker Compose", "Umgebungsvariablen", "Erster Admin", "Rollen und Zuweisungen",
            "Server anlegen, starten, stoppen, löschen", "Das Verwaltungstool als Server-Admin", "Server verwalten", "Versionen und Updates", "Telemetrie",
            "Ressourcen und Limits", "E-Mail, Passwort vergessen und Benachrichtigungen", "Zwei-Faktor-Anmeldung", "Server übernehmen und klonen",
            "REST-API und Tokens", "Backup des Verwaltungstools", "HTTPS und Reverse Proxy", "Das Verwaltungstool aktualisieren", "Fehlersuche",
        ], headings);
    }

    /// <summary>AC2: the manager's variables (and those of its compose files) are exactly the ones the README names.</summary>
    [Fact]
    public void Variables_ReadmeAndCode_Match()
    {
        var code = Directory.GetFiles(Path.Combine(RepoPaths.Root, "src", "OVS.Manager"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Concat(Directory.GetFiles(Path.Combine(RepoPaths.Root, "deploy", "manager"), "*.yml", SearchOption.AllDirectories))
            .SelectMany(file => ManagerVariable().Matches(File.ReadAllText(file)).Select(match => match.Value)).ToHashSet();
        var named = ManagerVariable().Matches(Readme()).Select(match => match.Value).ToHashSet();
        Assert.Equal(code.Order(StringComparer.Ordinal), named.Order(StringComparer.Ordinal));
        Assert.Contains("| `TZ` |", Readme(), StringComparison.Ordinal);
    }

    /// <summary>AC3: every repository path in the README exists.</summary>
    [Fact]
    public void NamedFiles_Readme_Exist()
    {
        var paths = RepoPath().Matches(Readme()).Select(match => match.Groups[1].Value).Distinct().ToList();
        Assert.Contains("deploy/manager/caddy/docker-compose.yml", paths);
        Assert.All(paths, path => Assert.True(File.Exists(Path.Combine(RepoPaths.Root, path)), $"{path} does not exist"));
    }

    /// <summary>AC4: README.md links to README-MANAGER.md and leaves the manager to it.</summary>
    [Fact]
    public void MainReadme_Manager_OnlyLinked()
    {
        var main = File.ReadAllText(Path.Combine(RepoPaths.Root, "README.md"));
        Assert.Contains("(README-MANAGER.md)", main, StringComparison.Ordinal);
        Assert.DoesNotMatch(ManagerVariable(), main);
    }

    [GeneratedRegex(@"\b(OVS_MANAGER_[A-Z_]+|SMTP_[A-Z_]+)\b")]
    private static partial Regex ManagerVariable();

    // a path in backticks that starts in the repository: a top-level file or a relative path with a slash
    [GeneratedRegex(@"`((?:deploy|src|docs|tests)/[A-Za-z0-9_./-]+|[A-Za-z0-9_-]+\.(?:md|manager))`")]
    private static partial Regex RepoPath();
}
