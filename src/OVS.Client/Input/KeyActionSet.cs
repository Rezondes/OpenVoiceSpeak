using System.Numerics;

namespace OVS.Client.Input;

/// <summary>Package 124: actions as bits, so a key poll allocates nothing and its result is published in one write.</summary>
public readonly record struct KeyActionSet(int Bits)
{
    public bool Contains(KeyAction action) => (Bits & Bit(action)) != 0;
    public KeyActionSet With(KeyAction action) => new(Bits | Bit(action));
    public KeyActionSet Without(KeyAction action) => new(Bits & ~Bit(action));
    public int Count => BitOperations.PopCount((uint)Bits);
    static int Bit(KeyAction action) => 1 << (int)action;
}
