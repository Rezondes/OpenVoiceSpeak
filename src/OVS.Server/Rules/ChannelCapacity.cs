namespace OVS.Server.Rules;

/// <summary>When a channel is full.</summary>
public static class ChannelCapacity
{
    /// <summary>Package 35: limited and no free slot; othersInside does not count the user who wants in.</summary>
    public static bool IsFull(int maxUsers, int othersInside) => maxUsers > 0 && othersInside >= maxUsers;
}
