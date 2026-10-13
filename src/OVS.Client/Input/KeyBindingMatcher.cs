namespace OVS.Client.Input;

/// <summary>Which key bindings the keys pressed right now trigger.</summary>
internal static class KeyBindingMatcher
{
    /// <summary>
    /// The actions whose chord is fully pressed. Extra modifiers do not hurt (Shift held while running still lets
    /// a plain PTT key work), but for one key only the bindings with the most matching modifiers count,
    /// so Ctrl+F1 does not also fire F1. Package 124: runs every 10 ms, so plain loops instead of LINQ (nothing allocated).
    /// </summary>
    public static KeyActionSet Resolve(IReadOnlyList<KeyBinding> bindings, Func<int, bool> isDown, ChordModifiers held)
    {
        var active = new KeyActionSet();
        for (int index = 0; index < bindings.Count; index++)
        {
            var binding = bindings[index];
            if (!Matches(binding, isDown, held)) continue;
            bool outranked = false;
            for (int otherIndex = 0; otherIndex < bindings.Count && !outranked; otherIndex++)
            {
                var other = bindings[otherIndex];
                outranked = other.Chord.Key == binding.Chord.Key && other.Chord.ModifierCount > binding.Chord.ModifierCount && Matches(other, isDown, held);
            }
            if (!outranked) active = active.With(binding.Action);
        }
        return active;
    }

    static bool Matches(KeyBinding binding, Func<int, bool> isDown, ChordModifiers held) =>
        binding.Chord.Key != 0 && (binding.Chord.Modifiers & held) == binding.Chord.Modifiers && isDown(binding.Chord.Key);
}
