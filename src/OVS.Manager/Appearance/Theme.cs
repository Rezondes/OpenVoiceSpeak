namespace OVS.Manager.Appearance;

/// <summary>Package M2: the colours of the manager's pages; <see cref="System"/> follows the browser.</summary>
internal enum Theme
{
    /// <summary>The browser's preference (<c>prefers-color-scheme</c>).</summary>
    System,
    /// <summary>Always light.</summary>
    Light,
    /// <summary>Always dark.</summary>
    Dark,
}
