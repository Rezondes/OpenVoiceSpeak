using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace OVS.Client.Layout;

/// <summary>
/// The settings and the administration as in Discord: a <see cref="TabControl"/> with the class <c>sideNav</c> lists its
/// pages on the left and shows the chosen one beside it. Where it is too narrow for both (the class <c>stacked</c>, set by
/// <see cref="Responsive.StackBelowProperty"/>), it shows either the list (class <c>listShown</c>) or the chosen page with
/// a back button, like a phone. Picking a page by click, tap or Enter opens it; the arrow keys only move the choice.
/// </summary>
internal static class SideNav
{
    /// <summary>The class that shows the list instead of the page while stacked.</summary>
    public const string ListShownClass = "listShown";

    /// <summary>On a tab control: list and page switch as described above.</summary>
    public static readonly AttachedProperty<bool> ListDetailProperty =
        AvaloniaProperty.RegisterAttached<TabControl, bool>("ListDetail", typeof(SideNav));

    /// <summary>Gets whether the tab control switches between list and page.</summary>
    public static bool GetListDetail(TabControl tabs) => tabs.GetValue(ListDetailProperty);
    /// <summary>Sets whether the tab control switches between list and page.</summary>
    public static void SetListDetail(TabControl tabs, bool value) => tabs.SetValue(ListDetailProperty, value);

    static SideNav() => ListDetailProperty.Changed.AddClassHandler<TabControl>((tabs, args) =>
    {
        if (!args.GetNewValue<bool>()) return;
        ShowList(tabs);
        // a page opened anew (a new view model) starts on the list again
        tabs.DataContextChanged += (_, _) => ShowList(tabs);
        tabs.AddHandler(InputElement.TappedEvent, (_, tapped) => OnPicked(tabs, tapped), RoutingStrategies.Bubble, handledEventsToo: true);
        tabs.AddHandler(InputElement.KeyDownEvent, (_, key) =>
        {
            if (key.Key is Key.Enter or Key.Space) OnPicked(tabs, key);
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        tabs.TemplateApplied += (_, applied) =>
        {
            if (applied.NameScope.Find<Button>("PART_Back") is { } back) back.Click += (_, _) => ShowList(tabs);
        };
    });

    /// <summary>Shows the list of pages (only seen while stacked).</summary>
    public static void ShowList(TabControl tabs) => tabs.Classes.Add(ListShownClass);

    /// <summary>Shows the chosen page (only seen while stacked).</summary>
    public static void ShowPage(TabControl tabs) => tabs.Classes.Remove(ListShownClass);

    /// <summary>A click, tap or key on one of the tab headers opens its page; one inside a page does not.</summary>
    static void OnPicked(TabControl tabs, RoutedEventArgs args)
    {
        if (args.Source is Visual source && source.GetSelfAndVisualAncestors().OfType<TabItem>().FirstOrDefault() is { } header
            && tabs.IndexFromContainer(header) >= 0)
            ShowPage(tabs);
    }
}
