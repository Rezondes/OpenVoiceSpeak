using System.Globalization;
using System.Text.RegularExpressions;

namespace OVS.Tests;

/// <summary>
/// One checked acceptance criterion of a PLAN.md package (N20): its package, number and line, and whether it carries a
/// manual marker right after its id ("(manuell", "(manual", "(spike"), which takes it out of the coverage check.
/// </summary>
sealed record PlanCriterion(int Package, int Number, int Line, bool IsManual)
{
    static readonly Regex PackageHeading = new(@"^## Package (\d+):");
    static readonly Regex Checked = new(@"^- \[x\] AC(\d+)(?<manual>\s*\((?:manuell|manual|spike))?", RegexOptions.IgnoreCase);

    /// <summary>The checked criteria of every <c>## Package N</c> chapter of <paramref name="plan"/>; unchecked ones are left out.</summary>
    public static List<PlanCriterion> Parse(string plan)
    {
        var criteria = new List<PlanCriterion>();
        int? package = null;
        var lines = plan.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].TrimEnd('\r');
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                var heading = PackageHeading.Match(line);
                package = heading.Success ? int.Parse(heading.Groups[1].Value, CultureInfo.InvariantCulture) : null;
                continue;
            }
            if (package is null || Checked.Match(line) is not { Success: true } criterion) continue;
            criteria.Add(new(package.Value, int.Parse(criterion.Groups[1].Value, CultureInfo.InvariantCulture), index + 1, criterion.Groups["manual"].Success));
        }
        return criteria;
    }

    /// <summary>The key of an exemption: "Package N AC m".</summary>
    public string Key => $"Package {Package} AC{Number}";
}
