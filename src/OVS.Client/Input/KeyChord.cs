using OVS.Client.Localization;
using System.Numerics;

namespace OVS.Client.Input;

/// <param name="Key">Windows virtual key code, mouse buttons included.</param>
public sealed record KeyChord(int Key, ChordModifiers Modifiers = ChordModifiers.None)
{
    public int ModifierCount => BitOperations.PopCount((uint)Modifiers);

    public string Name =>
        (Modifiers.HasFlag(ChordModifiers.Ctrl) ? Strings.Key_CtrlPlus : "") +
        (Modifiers.HasFlag(ChordModifiers.Shift) ? Strings.Key_ShiftPlus : "") +
        (Modifiers.HasFlag(ChordModifiers.Alt) ? "Alt+" : "") +
        KeyPoller.KeyName(Key);
}
