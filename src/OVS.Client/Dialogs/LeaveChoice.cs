namespace OVS.Client.Dialogs;

/// <summary>Package 114: the answer to "Ungespeicherte Änderungen": keep editing, save and leave, or leave without saving.</summary>
#pragma warning disable CA1515 // public: tests take it as a theory parameter, and xunit test methods are public
public enum LeaveChoice
#pragma warning restore CA1515
{
    /// <summary>Stay on the page.</summary>
    Stay,
    /// <summary>Save, then leave.</summary>
    Save,
    /// <summary>Leave without saving.</summary>
    Discard,
}
