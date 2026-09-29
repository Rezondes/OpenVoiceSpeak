namespace OVS.Client.Views;

/// <summary>Package 67: one channel row as measured: name text, visible small icons behind it (home, link), user count text.</summary>
public readonly record struct SidebarRow(double Name, int Icons, double Count);

/// <summary>Package 67 (A81): the sidebar width at which every channel row fits without trimming.</summary>
public static class SidebarWidth
{
    public const double Minimum = 240;

    /// <summary>
    /// Everything in a row besides name, small icons and count, matching MainWindow.axaml and Controls.axaml:
    /// sidebar border 1, ScrollViewer padding 2 × 8, row padding 2 × 8, channel icon 18 + 8 margin, gap 8 before the count.
    /// </summary>
    public const double Fixed = 1 + 16 + 16 + 26 + 8;

    /// <summary>A small icon (14) with its 6 px gap.</summary>
    public const double PerIcon = 6 + 14;

    public static double For(IEnumerable<SidebarRow> rows) =>
        Math.Max(Minimum, Math.Ceiling(rows.Select(r => r.Name + r.Icons * PerIcon + r.Count).DefaultIfEmpty(0).Max() + Fixed));
}
