namespace OVS.Client.Dialogs;

/// <summary>Package 114: the answer to "Ungespeicherte Änderungen": keep editing, save and leave, or leave without saving.</summary>
public enum LeaveChoice
{
    /// <summary>Stay on the page.</summary>
    Stay,
    /// <summary>Save, then leave.</summary>
    Save,
    /// <summary>Leave without saving.</summary>
    Discard,
}
