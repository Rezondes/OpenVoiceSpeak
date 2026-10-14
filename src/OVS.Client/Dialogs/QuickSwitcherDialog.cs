using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using OVS.Client.Controls;
using OVS.Client.Localization;
using OVS.Client.Models;
using OVS.Client.ViewModels;
using static OVS.Client.Dialogs.DialogControls;

namespace OVS.Client.Dialogs;

/// <summary>
/// Package 163: the quick switcher's card on the overlay (A20): a search field over the hits, Up and Down move through
/// them while the field keeps the focus, Enter opens the chosen one, Esc or a click beside it closes the card.
/// </summary>
internal static class QuickSwitcherDialog
{
    /// <summary>Shows the switcher; the chosen hit, null when closed without one.</summary>
    internal static Task<QuickSwitchHit?> ShowAsync(OverlayHost overlay, QuickSwitcherViewModel switcher)
    {
        var field = new TextBox { Name = "QuickSwitcherField", Watermark = Strings.Quick_Watermark };
        field.Bind(TextBox.TextProperty, new Binding(nameof(QuickSwitcherViewModel.Query)) { Mode = BindingMode.TwoWay });
        field.AddHandler(InputElement.KeyDownEvent, (_, args) =>
        {
            if (args.Key is not (Key.Up or Key.Down)) return;
            switcher.Move(args.Key == Key.Up ? -1 : 1);
            args.Handled = true;
        }, RoutingStrategies.Tunnel);
        var list = new ListBox
        {
            Name = "QuickSwitcherHits",
            MaxHeight = 320,
            ItemTemplate = new FuncDataTemplate<QuickSwitchHit>((hit, _) => hit is null ? null : new DockPanel
            {
                Children =
                {
                    DockAt(CreateText(hit.Detail, "caption"), Dock.Right),
                    new TextBlock { Text = hit.Title, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) },
                },
            }),
        };
        list.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(QuickSwitcherViewModel.Hits)));
        list.Bind(SelectingItemsControl.SelectedIndexProperty, new Binding(nameof(QuickSwitcherViewModel.SelectedIndex)) { Mode = BindingMode.TwoWay });
        list.Bind(Visual.IsVisibleProperty, new Binding(nameof(QuickSwitcherViewModel.HasNoHits)) { Converter = BoolConverters.Not });
        var none = CreateText(Strings.Quick_Nothing, "muted");
        none.Bind(Visual.IsVisibleProperty, new Binding(nameof(QuickSwitcherViewModel.HasNoHits)));
        var body = new StackPanel { Spacing = 8, DataContext = switcher, Children = { field, list, none } };
        return DialogFrame.ShowAsync(overlay, Strings.Quick_Title, "Search", body, () => switcher.Selected, new(Strings.Quick_Open));
    }
}
