using OVS.Client.Localization;

namespace OVS.Client.Input;

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
