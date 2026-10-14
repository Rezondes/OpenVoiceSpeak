using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OVS.Client.Layout;
using OVS.Client.Views;

namespace OVS.Tests.TestSupport;

/// <summary>Package 152: the settings are pages of a side navigation; a test opens the page that holds what it checks.</summary>
internal static class SettingsPages
{
    /// <summary>The side navigation of the settings page inside the root.</summary>
    public static TabControl Sections(Visual root) =>
        (root as SettingsView ?? root.GetVisualDescendants().OfType<SettingsView>().Single()).FindControl<TabControl>("Sections")!;

    /// <summary>The pages in their order, group titles left out.</summary>
    public static IReadOnlyList<TabItem> All(Visual root) =>
        Sections(root).Items.OfType<TabItem>().Where(tabItem => !tabItem.Classes.Contains("navGroup")).ToList();

    /// <summary>Chooses the page with this header and shows it (also when the list would be shown first).</summary>
    public static void Open(Visual root, string header)
    {
        var sections = Sections(root);
        sections.SelectedItem = All(root).Single(tabItem => tabItem.Header as string == header);
        SideNav.ShowPage(sections);
        Dispatcher.UIThread.RunJobs();
    }
}
