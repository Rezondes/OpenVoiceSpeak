namespace OVS.Client.ViewModels;

/// <summary>
/// A page of the page flow besides home: the settings page and the administration (Packages 104 and 114). The page flow
/// shows one at a time and asks it what a way out needs.
/// </summary>
internal interface IPageViewModel
{
    /// <summary>The page holds changes a way out would lose.</summary>
    bool HasChanges { get; }

    /// <summary>Saves the changes; false when they were not saved, and the page stays.</summary>
    Task<bool> SaveAsync();

    /// <summary>Lets go of what the page holds while shown; the page flow calls it when the page closes.</summary>
    void Detach();

    /// <summary>The page's own way out that asks first when something is not saved (the X, the close button, Esc).</summary>
    event Action? LeaveRequested;
}
