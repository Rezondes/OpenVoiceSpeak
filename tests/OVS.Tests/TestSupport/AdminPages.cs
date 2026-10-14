using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OVS.Client.Layout;
using OVS.Client.Views;

namespace OVS.Tests.TestSupport;

/// <summary>Package 154: the administration's pages in its side navigation; a test opens the page that holds what it checks.</summary>
internal static class AdminPages
{
    /// <summary>The side navigation of the administration inside the root.</summary>
    public static TabControl Tabs(Visual root) =>
        (root as AdminView ?? root.GetVisualDescendants().OfType<AdminView>().Single()).FindControl<TabControl>("Tabs")!;

    /// <summary>The pages in their order, group titles left out.</summary>
    public static IReadOnlyList<TabItem> All(Visual root) =>
        Tabs(root).Items.OfType<TabItem>().Where(tabItem => !tabItem.Classes.Contains(SideNav.GroupClass)).ToList();

    /// <summary>The page with this header.</summary>
    public static TabItem Find(Visual root, string header) => All(root).Single(tabItem => tabItem.Header as string == header);

    /// <summary>Chooses the page with this header and shows it (also when the list would be shown first).</summary>
    public static void Open(Visual root, string header) => Open(root, Find(root, header));

    /// <summary>Chooses this page and shows it.</summary>
    public static void Open(Visual root, TabItem page)
    {
        var tabs = Tabs(root);
        tabs.SelectedItem = page;
        SideNav.ShowPage(tabs);
        Dispatcher.UIThread.RunJobs();
    }
}
