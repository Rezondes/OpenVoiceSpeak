using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Xunit.Sdk;

namespace OVS.Tests.TestSupport;

/// <summary>
/// Package 68 (A93): layout checks for the responsive tests. Headless rendering lays out like the real window,
/// so a control that runs off the visible area shows up in its bounds.
/// </summary>
public static class LayoutAssert
{
    const double Tolerance = 1;

    /// <summary>
    /// Every visible button, slider, text box, check box, combo box and text block lies horizontally inside
    /// the viewport of its nearest ScrollViewer, or inside the window when there is none.
    /// Package 78: controls inside <paramref name="except"/> are skipped (e.g. a matrix that scrolls on purpose).
    /// </summary>
    public static void FitsHorizontally(Visual root, Visual? except = null)
    {
        var top = TopLevel.GetTopLevel(root) ?? (Visual)root;
        var offenders = new List<string>();
        foreach (var control in root.GetVisualDescendants().OfType<Control>())
        {
            if (control is not (Button or Slider or TextBox or CheckBox or ComboBox or TextBlock)) continue;
            if (!control.IsEffectivelyVisible || control.Bounds.Width <= 0 || control.Bounds.Height <= 0) continue;
            if (except is not null && except.IsVisualAncestorOf(control)) continue;
            var container = control.FindAncestorOfType<ScrollViewer>() is { } scroller ? (Visual)scroller : top;
            if (Horizontal(container, top) is not var (left, right) || Horizontal(control, top) is not var (x, end)) continue;
            if (x < left - Tolerance || end > right + Tolerance)
                offenders.Add($"{control.GetType().Name} {Describe(control)} at {x:0.#}..{end:0.#}, visible {left:0.#}..{right:0.#}");
            // Text wider than its own box is cut off without "..." (trimmed text is fine)
            else if (control is TextBlock { TextTrimming: var trimming } text && trimming == TextTrimming.None
                     && Needed(text) > text.Bounds.Width - text.Padding.Left - text.Padding.Right + Tolerance)
                offenders.Add($"{control.GetType().Name} {Describe(control)} needs {Needed(text):0.#}, has {text.Bounds.Width:0.#}");
        }
        if (offenders.Count > 0) throw new XunitException("Outside the visible area:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>Package 78: a wrapped line may end in the space it broke at, which is not drawn.</summary>
    static double Needed(TextBlock text) =>
        text.TextWrapping == TextWrapping.NoWrap ? text.TextLayout.WidthIncludingTrailingWhitespace : text.TextLayout.Width;

    /// <summary>
    /// Left and right edge in window coordinates; a ScrollViewer counts with its viewport (without the scroll bar).
    /// Package 104: from the layout, not from where a running animation draws it (a page sliding in).
    /// </summary>
    static (double Left, double Right)? Horizontal(Visual visual, Visual top)
    {
        var width = visual is ScrollViewer { Viewport.Width: > 0 } s ? s.Viewport.Width : visual.Bounds.Width;
        return X(visual, top) is { } x ? (x, x + width) : null;
    }

    /// <summary>Package 105: the top edge from the layout, relative to an ancestor (null when it is none).</summary>
    public static double? Y(Visual visual, Visual ancestor)
    {
        double y = 0;
        Visual? at = visual;
        for (; at is not null && at != ancestor; at = at.GetVisualParent()) y += at.Bounds.Y;
        return at == ancestor ? y : null;
    }

    /// <summary>Package 104: the left edge from the layout, relative to an ancestor (null when it is none).</summary>
    public static double? X(Visual visual, Visual ancestor)
    {
        double x = 0;
        Visual? at = visual;
        for (; at is not null && at != ancestor; at = at.GetVisualParent()) x += at.Bounds.X;
        return at == ancestor ? x : null;
    }

    static string Describe(Control control)
    {
        var text = control switch
        {
            TextBlock t => t.Text,
            ContentControl { Content: string s } => s,
            ContentControl c => c.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => !string.IsNullOrEmpty(t.Text))?.Text,
            _ => null,
        };
        return $"'{control.Name ?? Avalonia.Automation.AutomationProperties.GetName(control)}' \"{text}\"";
    }
}
