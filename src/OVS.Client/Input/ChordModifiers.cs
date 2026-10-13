namespace OVS.Client.Input;

/// <summary>The modifier keys of a chord, as flags.</summary>
[Flags]
internal enum ChordModifiers
{
    /// <summary>No modifier.</summary>
    None = 0,
    /// <summary>A Ctrl key.</summary>
    Ctrl = 1,
    /// <summary>A Shift key.</summary>
    Shift = 2,
    /// <summary>An Alt key.</summary>
    Alt = 4,
}
