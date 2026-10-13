using OVS.Server.Rules;

namespace OVS.Tests.Server.Rules;

/// <summary>An inclusive range of whole numbers a server limit may take.</summary>
public sealed class ValueRangeTests
{
    [Theory]
    [InlineData(1, true)]
    [InlineData(5, true)]
    [InlineData(10, true)]
    [InlineData(0, false)]
    [InlineData(11, false)]
    [InlineData(int.MinValue, false)]
    [InlineData(int.MaxValue, false)]
    public void Contains_Value_TrueOnlyFromMinToMaxBothIncluded(int value, bool inside)
    {
        Assert.Equal(inside, new ValueRange(1, 10).Contains(value));
    }

    [Fact]
    public void Contains_SingleValueRange_OnlyThatValue()
    {
        var range = new ValueRange(0, 0);

        Assert.True(range.Contains(0));
        Assert.False(range.Contains(1));
        Assert.False(range.Contains(-1));
    }
}
