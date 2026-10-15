using OVS.Client.Input;

namespace OVS.Client.Dialogs;

/// <summary>The dialogs the page flow asks, and the one it hands to the settings page. Unset entries mean "cancelled" (used by tests).</summary>
internal sealed record PageFlowDialogs
{
    /// <summary>Package 114: unsaved changes would be lost; unset means leave without saving (as before).</summary>
    public Func<Task<LeaveChoice>>? AskLeave { get; init; }

    /// <summary>Package 41: action and key; null binding = add. The second argument captures the next key.</summary>
    public Func<KeyBinding?, Func<KeyAction, Task<KeyChord?>>, Task<KeyBinding?>>? EditKeyBinding { get; init; }

    /// <summary>Package 171: the changes Discord's settings would make, each checked; returns the checked ones.</summary>
    public Func<Models.ImportPlan, Task<IReadOnlyList<Models.ImportChange>?>>? AskImport { get; init; }
}
