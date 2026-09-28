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
    /// so Ctrl+F1 does not also fire F1.
    /// </summary>
    public static HashSet<KeyAction> Resolve(IEnumerable<KeyBinding> bindings, Func<int, bool> isDown, ChordModifiers held) =>
        bindings
            .Where(b => b.Chord.Key != 0 && (b.Chord.Modifiers & held) == b.Chord.Modifiers && isDown(b.Chord.Key))
            .GroupBy(b => b.Chord.Key)
            .SelectMany(g =>
            {
                int most = g.Max(b => b.Chord.ModifierCount);
                return g.Where(b => b.Chord.ModifierCount == most);
            })
            .Select(b => b.Action)
            .ToHashSet();
}

/// <summary>Turns the active actions of successive polls into "a hold action changed" and single presses.</summary>
public sealed class KeyStateTracker
{
    HashSet<KeyAction> previous = [];

    /// <returns>Toggle actions pressed since the last poll, and whether any hold action changed.</returns>
    public (IReadOnlyList<KeyAction> Pressed, bool HoldChanged) Update(IReadOnlySet<KeyAction> active)
    {
        var pressed = active.Where(a => !KeyActions.IsHold(a) && !previous.Contains(a)).ToList();
        bool holdChanged = KeyActions.All.Where(KeyActions.IsHold).Any(a => active.Contains(a) != previous.Contains(a));
        previous = active.ToHashSet();
        return (pressed, holdChanged);
    }
}
