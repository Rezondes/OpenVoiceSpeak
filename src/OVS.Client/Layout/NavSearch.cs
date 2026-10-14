using Avalonia.Controls;
using Avalonia.LogicalTree;
using OVS.Client.Models;

namespace OVS.Client.Layout;

/// <summary>
/// Package 158: finds a setting by the words the user sees. The index is built from the pages of a side navigation
/// (<see cref="SideNav"/>): every label, section, caption and hint text and every check box and radio button with its
/// page, in the UI language, so no second list of keywords has to follow the texts.
/// </summary>
internal sealed class NavSearch
{
    /// <summary>The most hits shown at once.</summary>
    public const int MaxHits = 8;

    static readonly string[] TextClasses = ["label", "section", "caption"];

    readonly List<NavSearchHit> entries = [];

    /// <summary>The index of the pages of <paramref name="tabs"/>, group titles left out.</summary>
    public NavSearch(TabControl tabs)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        foreach (var page in tabs.Items.OfType<TabItem>().Where(tabItem => !tabItem.Classes.Contains(SideNav.GroupClass)))
        {
            if (page.Content is not ILogical content) continue;
            foreach (var control in content.GetSelfAndLogicalDescendants().OfType<Control>())
            {
                // a check box or radio button found by its text, also when the text is a text block inside it
                var choice = control is TextBlock { Parent: CheckBox or RadioButton } inner ? (Control)inner.Parent! : null;
                var text = control switch
                {
                    TextBlock textBlock when choice is not null => textBlock.Text,
                    TextBlock textBlock when TextClasses.Any(textBlock.Classes.Contains) => textBlock.Text,
                    ContentControl { Content: string label } and (CheckBox or RadioButton) => label,
                    _ => null,
                };
                if (!string.IsNullOrWhiteSpace(text)) entries.Add(new NavSearchHit(text, page, choice ?? control));
            }
        }
    }

    /// <summary>The hits for <paramref name="query"/>: the same text first, then texts and words that start with it, then any that hold it; at most <see cref="MaxHits"/>.</summary>
    public IReadOnlyList<NavSearchHit> Find(string? query) =>
        entries
            .Select(entry => new { Entry = entry, Rank = TextMatch.Rank(entry.Text, query) })
            .Where(candidate => candidate.Rank is not null)
            .OrderBy(candidate => candidate.Rank)
            .Select(candidate => candidate.Entry)
            .DistinctBy(entry => (entry.Text, entry.Page))
            .Take(MaxHits)
            .ToList();
}
