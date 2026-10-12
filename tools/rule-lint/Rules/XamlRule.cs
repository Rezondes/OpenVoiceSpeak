using System.Text.RegularExpressions;

namespace RuleLint.Rules;

/// <summary>
/// Rules 12 and 13 in XAML: user-facing text comes from the resources, not from a literal attribute (a warning, since a
/// symbol or a product name may stand there on purpose), and each namespace uses its fixed prefix.
/// </summary>
public sealed partial class XamlRule : IRule
{
    readonly LintSettings settings;

    /// <summary>The rule with the project's prefixes and text attributes.</summary>
    public XamlRule(LintSettings settings)
    {
        this.settings = settings;
    }

    /// <inheritdoc/>
    public IEnumerable<Finding> Check(SourceFile file, RepositoryIndex index)
    {
        if (!file.IsXaml) yield break;
        foreach (Match match in BuildPrefixPattern().Matches(file.Text))
        {
            var prefix = match.Groups["prefix"].Value;
            var space = match.Groups["space"].Value;
            var fixedPrefix = settings.XamlPrefixes.FirstOrDefault(entry => entry.Value == space).Key;
            if (settings.XamlPrefixes.TryGetValue(prefix, out var fixedSpace) && fixedSpace != space)
            {
                yield return Finding.CreateAtOffset(file, match.Index, "RL131", Severity.Error,
                    $"Prefix '{prefix}' stands for '{fixedSpace}', not '{space}' (rule 13).", prefix);
            }
            else if (fixedPrefix is not null && fixedPrefix != prefix)
            {
                yield return Finding.CreateAtOffset(file, match.Index, "RL131", Severity.Error,
                    $"Namespace '{space}' has the fixed prefix '{fixedPrefix}', not '{prefix}' (rule 13).", prefix);
            }
        }
        if (file.Root!.Kind != RootKind.Source) yield break;
        foreach (Match match in BuildAttributePattern().Matches(file.Text))
        {
            var name = match.Groups["name"].Value;
            var value = match.Groups["value"].Value;
            if (!settings.XamlTextAttributes.Contains(name) || value.StartsWith('{') || !value.Any(char.IsLetter)) continue;
            yield return Finding.CreateAtOffset(file, match.Index + 1, "RL121", Severity.Warning,
                $"{name}=\"{value}\" is literal user-facing text; take it from the localization resources (rule 12).", name + "=" + value);
        }
    }

    [GeneratedRegex(@"xmlns:(?<prefix>\w+)=""using:(?<space>[\w.]+)""")]
    private static partial Regex BuildPrefixPattern();

    [GeneratedRegex(@"\s(?<name>[\w.]+)=""(?<value>[^""]*)""")]
    private static partial Regex BuildAttributePattern();
}
