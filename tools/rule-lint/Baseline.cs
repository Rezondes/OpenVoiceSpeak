using System.Text.Json;

namespace RuleLint;

/// <summary>
/// The known findings of the code as it was when the rules came in, so the checker fails only on new ones while a
/// refactoring removes the old ones. A finding is known when the baseline holds its key (rule, path, subject) at least as
/// often as the code has it.
/// </summary>
internal sealed class Baseline
{
    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    readonly Dictionary<string, int> allowed;

    Baseline(Dictionary<string, int> allowed)
    {
        this.allowed = allowed;
    }

    /// <summary>A baseline that knows no finding.</summary>
    public static Baseline Empty => new([]);

    /// <summary>The baseline in <paramref name="path"/>, or an empty one when the file does not exist.</summary>
    public static Baseline Load(string path) => File.Exists(path) ? Parse(File.ReadAllText(path)) : Empty;

    /// <summary>The baseline in the JSON <paramref name="json"/>; entries with the same key add up.</summary>
    public static Baseline Parse(string json)
    {
        var entries = JsonSerializer.Deserialize<List<BaselineEntry>>(json, JsonOptions) ?? [];
        return new Baseline(entries.GroupBy(entry => entry.Key).ToDictionary(group => group.Key, group => group.Sum(entry => entry.Count)));
    }

    /// <summary>The baseline JSON that knows exactly <paramref name="findings"/>, ordered by key.</summary>
    public static string Write(IEnumerable<Finding> findings) =>
        JsonSerializer.Serialize(
            findings.GroupBy(finding => finding.Key)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new BaselineEntry(group.Key, group.Count()))
                .ToList(),
            JsonOptions);

    /// <summary>Splits <paramref name="findings"/> into new and known ones and lists the known keys that are gone.</summary>
    public BaselineResult Apply(IReadOnlyList<Finding> findings)
    {
        var left = new Dictionary<string, int>(allowed);
        var fresh = new List<Finding>();
        var known = 0;
        foreach (var finding in findings)
        {
            if (left.TryGetValue(finding.Key, out var count) && count > 0)
            {
                left[finding.Key] = count - 1;
                known++;
            }
            else
            {
                fresh.Add(finding);
            }
        }
        var gone = left.Where(entry => entry.Value > 0).Select(entry => entry.Key).Order(StringComparer.Ordinal).ToList();
        return new BaselineResult(fresh, known, gone);
    }
}
