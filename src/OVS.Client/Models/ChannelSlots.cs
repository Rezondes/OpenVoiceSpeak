using OVS.Shared.Permissions;

namespace OVS.Client.Models;

/// <summary>
/// Package 35: how many users a channel holds and its limit (<paramref name="MaxUsers"/> 0 = unlimited), as its slot counter
/// shows them.
/// </summary>
public readonly record struct ChannelSlots(int UserCount, int MaxUsers)
{
    /// <summary>Package 108: no free slot left (the counter pulses once when it gets there); never without a limit.</summary>
    public bool IsFull => ChannelCapacity.IsFull(MaxUsers, UserCount);

    /// <summary>"3/10" is 3 of at most 10, "3" is 3 without a limit.</summary>
    public override string ToString() => MaxUsers > 0 ? $"{UserCount}/{MaxUsers}" : UserCount.ToString();
}
