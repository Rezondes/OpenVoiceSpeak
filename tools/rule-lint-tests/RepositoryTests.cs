namespace RuleLint.Tests;

/// <summary>
/// The rule checker on this repository, so the full test run checks the rules too: it fails on every new error (a
/// finding the baseline does not know) and prints them in the checker's form.
/// </summary>
public sealed class RepositoryTests
{
    [Fact]
    public void Repository_AllRules_HaveNoNewErrors()
    {
        var repository = RepositoryLocator.Find(AppContext.BaseDirectory) ?? throw new InvalidOperationException("No rule-lint.json above the test output.");
        var settings = LintSettings.Load(Path.Combine(repository, LintSettings.FileName));
        var findings = Linter.Run(settings, SourceScanner.Scan(repository, settings));
        var result = Baseline.Load(Path.Combine(repository, settings.BaselineFile)).Apply(findings);
        var errors = result.New.Where(finding => finding.Severity == Severity.Error).Select(finding => finding.Format()).ToList();
        Assert.True(errors.Count == 0, "New rule violations (dotnet run --project tools/rule-lint):\n" + string.Join("\n", errors));
    }
}
