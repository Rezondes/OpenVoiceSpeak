using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;
using OVS.Client.ViewModels;

namespace OVS.Tests.TestSupport;

/// <summary>Rows of the channel tree, found by name, and where to point at them.</summary>
static class Rows
{
    /// <summary>The channel's own row (not its users), found by name.</summary>
    public static Border ChannelRow(Visual root, string name) => root.GetVisualDescendants().OfType<Border>()
        .Single(b => b.Classes.Contains("row") && b.DataContext is ChannelViewModel c && c.Name == name);

    /// <summary>The user's row, found by nickname; not a row that is leaving.</summary>
    public static Border UserRow(Visual root, string nickname) => root.GetVisualDescendants().OfType<Border>()
        .Single(b => b.Classes.Contains("row") && b.DataContext is UserViewModel u && u.Nickname == nickname
                     && b.FindAncestorOfType<ContentPresenter>() is { } c && !c.Classes.Contains("leaving"));

    /// <summary>The center of <paramref name="control"/> in the coordinates of <paramref name="root"/>.</summary>
    public static Point Center(Control control, Visual root) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root)!.Value;
}
