namespace OVS.Client.Models;

/// <summary>Package 122: what a state message changed, so whoever listens only redoes what depends on it.</summary>
[Flags]
public enum StateChange
{
    /// <summary>Nothing changed.</summary>
    None = 0,
    /// <summary>Someone joined or left.</summary>
    Members = 1,
    /// <summary>Someone was renamed.</summary>
    Names = 2,
    /// <summary>A channel's name, count or icons, the channel list or my own name: what the sidebar measures.</summary>
    Sidebar = 4,
    /// <summary>Rebuilt as a whole: the own rights, channel or groups, links or the settings may have changed too.</summary>
    Tree = 8,
    /// <summary>Everything may have changed.</summary>
    All = Members | Names | Sidebar | Tree,
}
