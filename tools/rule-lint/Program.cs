using RuleLint;

// The rule checker: dotnet run --project tools/rule-lint [-- --root <repository>] [--update-baseline]
// Prints every new finding as "path(line,column): error|warning RLnnn: message", then a summary line.
// Exit code: 0 no new error, 1 new errors, 2 wrong arguments or no settings file.
string? repository = null;
var updateBaseline = false;
for (var position = 0; position < args.Length; position++)
{
    if (args[position] == "--root" && position + 1 < args.Length)
    {
        repository = args[++position];
    }
    else if (args[position] == "--update-baseline")
    {
        updateBaseline = true;
    }
    else
    {
        Console.Error.WriteLine($"rule-lint: unknown argument '{args[position]}'. Usage: rule-lint [--root <repository>] [--update-baseline]");
        return 2;
    }
}

repository ??= RepositoryLocator.Find(Directory.GetCurrentDirectory());
if (repository is null || !File.Exists(Path.Combine(repository, LintSettings.FileName)))
{
    Console.Error.WriteLine($"rule-lint: no {LintSettings.FileName} found at or above the current folder.");
    return 2;
}

var settings = LintSettings.Load(Path.Combine(repository, LintSettings.FileName));
var findings = Linter.Run(settings, SourceScanner.Scan(repository, settings));
var baselinePath = Path.Combine(repository, settings.BaselineFile);
if (updateBaseline)
{
    File.WriteAllText(baselinePath, Baseline.Write(findings) + "\n");
    Console.WriteLine($"rule-lint: baseline written with {findings.Count} known findings to {settings.BaselineFile}.");
    return 0;
}

var result = Baseline.Load(baselinePath).Apply(findings);
foreach (var finding in result.New) Console.WriteLine(finding.Format());
var errors = result.New.Count(finding => finding.Severity == Severity.Error);
Console.WriteLine(
    $"rule-lint: {errors} errors, {result.New.Count - errors} warnings, {result.Known} known in the baseline"
    + (result.Fixed.Count > 0 ? $", {result.Fixed.Count} baseline keys fixed (run with --update-baseline to drop them)." : "."));
return errors > 0 ? 1 : 0;
