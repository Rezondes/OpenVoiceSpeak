using OVS.Server.Rules;

namespace OVS.Tests.Server.Rules;

public class ChannelCapacityTests
{
    [Theory]
    [InlineData(0, 50, false)] // unlimited
    [InlineData(2, 1, false)]
    [InlineData(2, 2, true)]
    [InlineData(2, 3, true)] // lowered below the count: still full
    public void IsFull_LimitAndOthersInside_FullWhenNoFreeSlot(int maxUsers, int othersInside, bool full) =>
        Assert.Equal(full, ChannelCapacity.IsFull(maxUsers, othersInside));
}
