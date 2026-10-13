using OVS.Client.Localization;
using System.Numerics;

namespace OVS.Client.Input;

/// <param name="Key">Windows virtual key code, mouse buttons included.</param>
/// <param name="Modifiers">The modifier keys held with it.</param>
internal sealed record KeyChord(int Key, ChordModifiers Modifiers = ChordModifiers.None)
{
    /// <summary>The modifiers in the chord.</summary>
    public int ModifierCount => BitOperations.PopCount((uint)Modifiers);

    /// <summary>The chord as shown, e.g. "Strg + F1".</summary>
    public string Name =>
        (Modifiers.HasFlag(ChordModifiers.Ctrl) ? Strings.Key_CtrlPlus : "") +
        (Modifiers.HasFlag(ChordModifiers.Shift) ? Strings.Key_ShiftPlus : "") +
        (Modifiers.HasFlag(ChordModifiers.Alt) ? "Alt+" : "") +
        VirtualKeys.Localize(Key);
}
