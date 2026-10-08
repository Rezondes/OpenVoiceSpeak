namespace OVS.Client.Input;

/// <summary>Turns the active actions of successive polls into "a hold action changed" and single presses.</summary>
public sealed class KeyStateTracker
{
    KeyActionSet previous;

    /// <returns>Toggle actions pressed since the last poll, and whether any hold action changed.</returns>
    public (KeyActionSet Pressed, bool HoldChanged) Update(KeyActionSet active)
    {
        int changed = active.Bits ^ previous.Bits;
        previous = active;
        return (new KeyActionSet(changed & active.Bits & ~KeyActionTraits.Holds.Bits), (changed & KeyActionTraits.Holds.Bits) != 0);
    }
}
