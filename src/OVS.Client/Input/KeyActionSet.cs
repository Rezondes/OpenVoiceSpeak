using System.Numerics;

namespace OVS.Client.Input;

/// <summary>Package 124: actions as bits, so a key poll allocates nothing and its result is published in one write.</summary>
internal readonly record struct KeyActionSet(int Bits)
{
    /// <summary>True when the set holds action.</summary>
    public bool Contains(KeyAction action) => (Bits & Bit(action)) != 0;
    /// <summary>The set with action.</summary>
    public KeyActionSet With(KeyAction action) => new(Bits | Bit(action));
    /// <summary>The set without action.</summary>
    public KeyActionSet Without(KeyAction action) => new(Bits & ~Bit(action));
    /// <summary>The actions in the set.</summary>
    public int Count => BitOperations.PopCount((uint)Bits);
    static int Bit(KeyAction action) => 1 << (int)action;
}
