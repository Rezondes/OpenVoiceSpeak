using OVS.Client.Models;
using OVS.Shared.Permissions;

namespace OVS.Tests.Client.Models;

/// <summary>Package 35, 108: the slot counter's numbers, its text and when the channel counts as full.</summary>
public sealed class ChannelSlotsTests
{
    [Theory]
    [InlineData(3, 10, "3/10")]
    [InlineData(3, 0, "3")]
    [InlineData(0, 0, "0")]
    public void ToString_LimitedOrUnlimited_CountOfLimitOrCountAlone(int users, int max, string text) =>
        Assert.Equal(text, new ChannelSlots(users, max).ToString());

    /// <summary>The rule and its limits are pinned in <see cref="OVS.Tests.Shared.Permissions.ChannelCapacityTests"/>.</summary>
    [Theory]
    [InlineData(3, 3)]
    [InlineData(2, 3)]
    public void IsFull_AnyNumbers_AsTheSharedCapacityRule(int users, int max) =>
        Assert.Equal(ChannelCapacity.IsFull(max, users), new ChannelSlots(users, max).IsFull);
}
