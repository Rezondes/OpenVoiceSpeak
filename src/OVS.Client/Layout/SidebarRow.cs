namespace OVS.Client.Layout;

/// <summary>Package 67: one channel row as measured: name text, visible small icons behind it (home, link, lock), user count text.</summary>
internal readonly record struct SidebarRow(double Name, int Icons, double Count);
