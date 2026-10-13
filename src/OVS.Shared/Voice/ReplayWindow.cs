namespace OVS.Shared.Voice;

/// <summary>Sliding 64-packet anti-replay window.</summary>
public sealed class ReplayWindow
{
    bool any;
    uint highest;
    ulong seen;

    /// <summary>True the first time seq is seen within the window; false for a repeat or anything 64 or more behind the highest.</summary>
    public bool Accept(uint seq)
    {
        if (!any)
        {
            any = true;
            highest = seq;
            seen = 1;
            return true;
        }
        if (seq > highest)
        {
            uint shift = seq - highest;
            seen = shift >= 64 ? 1 : (seen << (int)shift) | 1;
            highest = seq;
            return true;
        }
        uint age = highest - seq;
        if (age >= 64) return false;
        ulong bit = 1UL << (int)age;
        if ((seen & bit) != 0) return false;
        seen |= bit;
        return true;
    }
}
