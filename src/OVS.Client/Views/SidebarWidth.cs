namespace OVS.Client.Views;

/// <summary>Package 67: one channel row as measured: name text, visible small icons behind it (home, link, lock), user count text.</summary>
public readonly record struct SidebarRow(double Name, int Icons, double Count);

/// <summary>Package 67 (A81): the sidebar width at which every channel row fits without trimming.</summary>
public static class SidebarWidth
{
    public const double Minimum = 240;

    /// <summary>
    /// Everything in a row besides name, small icons and count, matching MainWindow.axaml and Controls.axaml:
    /// sidebar border 1, ScrollViewer padding 2 × 8, row padding 2 × 8, channel icon 18 + 8 margin, gap 8 before the count.
    /// </summary>
    public const double Fixed = Border + 16 + 16 + 26 + 8;

    /// <summary>The sidebar's right border.</summary>
    public const double Border = 1;

    /// <summary>A small icon (14) with its 6 px gap.</summary>
    public const double PerIcon = 6 + 14;

    /// <param name="footer">Package 113: what the footer (own user, talk hint, buttons) needs, measured by the window.</param>
    public static double For(IEnumerable<SidebarRow> rows, double footer = 0) =>
        Math.Max(Math.Max(Minimum, Math.Ceiling(footer)), Math.Ceiling(rows.Select(r => r.Name + r.Icons * PerIcon + r.Count).DefaultIfEmpty(0).Max() + Fixed));
}
