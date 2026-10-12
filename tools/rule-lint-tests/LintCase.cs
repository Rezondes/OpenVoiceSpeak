namespace RuleLint.Tests;

/// <summary>
/// Runs the checker on files held in memory, in a small repository with one production root (<c>src/App</c>, namespace
/// <c>App</c>) and one test root (<c>tests/App.Tests</c>, namespace <c>App.Tests</c>).
/// </summary>
static class LintCase
{
    /// <summary>The settings of the small repository, with the extra JSON members <paramref name="json"/>.</summary>
    public static LintSettings CreateSettings(string json = "") => LintSettings.Parse(
        """
        {
          "roots": [
            { "folder": "src/App", "rootNamespace": "App", "kind": "Source" },
            { "folder": "tests/App.Tests", "rootNamespace": "App.Tests", "kind": "Test" }
          ]
        """ + json + "}");

    /// <summary>A file at <paramref name="path"/> with <paramref name="text"/>, in the root of the small repository it lies in.</summary>
    public static SourceFile CreateFile(string path, string text, LintSettings? settings = null) =>
        new(path, text, SourceScanner.FindRoot(path, settings ?? CreateSettings()));

    /// <summary>Every finding of every rule in <paramref name="files"/>.</summary>
    public static List<Finding> Run(params SourceFile[] files) => Linter.Run(CreateSettings(), files);

    /// <summary>Every finding of every rule in <paramref name="files"/> under <paramref name="settings"/>.</summary>
    public static List<Finding> Run(LintSettings settings, params SourceFile[] files) => Linter.Run(settings, files);

    /// <summary>The rule ids of the findings of one file at <paramref name="path"/> with <paramref name="text"/>.</summary>
    public static List<string> ListRuleIds(string path, string text) => [.. Run(CreateFile(path, text)).Select(finding => finding.RuleId)];
}
