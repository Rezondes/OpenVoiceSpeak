using OVS.Client.Localization;
using System.Numerics;

namespace OVS.Client.Input;

/// <summary>What a key can do. Hold actions last while the key is down, the others fire once per press.</summary>
public enum KeyAction { PushToTalk, LinkPushToTalk, PushToMute, ToggleMute, ToggleDeafen }

[Flags]
public enum ChordModifiers { None = 0, Ctrl = 1, Shift = 2, Alt = 4 }

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

public sealed record KeyBinding(KeyAction Action, KeyChord Chord);

/// <summary>Package 59: the push-to-talk key a new profile starts with.</summary>
public static class DefaultKeys
{
    /// <summary>Mouse 4 when there are side buttons, else Right Ctrl: rarely used by games and harmless while typing.</summary>
    public static KeyChord PushToTalk(int mouseButtons) => new(mouseButtons >= 5 ? KeyPoller.VkXButton1 : KeyPoller.VkRControl);
}

public static class KeyActions
{
    public static readonly IReadOnlyList<KeyAction> All = Enum.GetValues<KeyAction>();

    public static bool IsHold(KeyAction action) => action is KeyAction.PushToTalk or KeyAction.LinkPushToTalk or KeyAction.PushToMute;

    public static string Label(KeyAction action) => action switch
    {
        KeyAction.PushToTalk => Strings.KeyAction_PushToTalk,
        KeyAction.LinkPushToTalk => Strings.KeyAction_LinkPushToTalk,
        KeyAction.PushToMute => Strings.KeyAction_PushToMute,
        KeyAction.ToggleMute => Strings.KeyAction_ToggleMute,
        KeyAction.ToggleDeafen => Strings.KeyAction_ToggleDeafen,
        _ => action.ToString(),
    };

    /// <summary>
    /// The actions whose chord is fully pressed. Extra modifiers do not hurt (Shift held while running still lets
    /// a plain PTT key work), but for one key only the bindings with the most matching modifiers count,
    /// so Ctrl+F1 does not also fire F1. Package 124: runs every 10 ms, so plain loops instead of LINQ (nothing allocated).
    /// </summary>
    public static KeyActionSet Resolve(IReadOnlyList<KeyBinding> bindings, Func<int, bool> isDown, ChordModifiers held)
    {
        var active = new KeyActionSet();
        for (int i = 0; i < bindings.Count; i++)
        {
            var binding = bindings[i];
            if (!Matches(binding, isDown, held)) continue;
            bool outranked = false;
            for (int j = 0; j < bindings.Count && !outranked; j++)
            {
                var other = bindings[j];
                outranked = other.Chord.Key == binding.Chord.Key && other.Chord.ModifierCount > binding.Chord.ModifierCount && Matches(other, isDown, held);
            }
            if (!outranked) active = active.With(binding.Action);
        }
        return active;
    }

    static bool Matches(KeyBinding binding, Func<int, bool> isDown, ChordModifiers held) =>
        binding.Chord.Key != 0 && (binding.Chord.Modifiers & held) == binding.Chord.Modifiers && isDown(binding.Chord.Key);

    /// <summary>The hold actions (<see cref="IsHold"/>) as one set.</summary>
    public static readonly KeyActionSet Holds = new KeyActionSet().With(KeyAction.PushToTalk).With(KeyAction.LinkPushToTalk).With(KeyAction.PushToMute);
}

/// <summary>Package 124: actions as bits, so a key poll allocates nothing and its result is published in one write.</summary>
public readonly record struct KeyActionSet(int Bits)
{
    public bool Contains(KeyAction action) => (Bits & Bit(action)) != 0;
    public KeyActionSet With(KeyAction action) => new(Bits | Bit(action));
    public KeyActionSet Without(KeyAction action) => new(Bits & ~Bit(action));
    public int Count => BitOperations.PopCount((uint)Bits);
    static int Bit(KeyAction action) => 1 << (int)action;
}

/// <summary>Turns the active actions of successive polls into "a hold action changed" and single presses.</summary>
public sealed class KeyStateTracker
{
    KeyActionSet previous;

    /// <returns>Toggle actions pressed since the last poll, and whether any hold action changed.</returns>
    public (KeyActionSet Pressed, bool HoldChanged) Update(KeyActionSet active)
    {
        int changed = active.Bits ^ previous.Bits;
        previous = active;
        return (new KeyActionSet(changed & active.Bits & ~KeyActions.Holds.Bits), (changed & KeyActions.Holds.Bits) != 0);
    }
}
