using OVS.Shared.Net;

namespace OVS.Tests.Shared.Net;

/// <summary>An undirected link of two channels, one key in either direction.</summary>
public sealed class ChannelPairTests
{
    static readonly Guid Low = Guid.Parse("00000000-0000-0000-0000-000000000001");
    static readonly Guid High = Guid.Parse("00000000-0000-0000-0000-000000000002");
    static readonly Guid Third = Guid.Parse("00000000-0000-0000-0000-000000000003");

    [Fact]
    public void Of_EitherDirection_SamePairInOrder()
    {
        Assert.Equal(new ChannelPair(Low, High), ChannelPair.Of(High, Low));
        Assert.Equal(ChannelPair.Of(Low, High), ChannelPair.Of(High, Low));
    }

    [Fact]
    public void Of_SameChannelTwice_KeepsBothEnds()
    {
        Assert.Equal(new ChannelPair(Low, Low), ChannelPair.Of(Low, Low));
    }

    [Fact]
    public void Touches_EitherEndOrNeither_TrueOnlyForAnEnd()
    {
        var pair = ChannelPair.Of(Low, High);

        Assert.True(pair.Touches(Low));
        Assert.True(pair.Touches(High));
        Assert.False(pair.Touches(Third));
    }

    [Fact]
    public void Other_OneEnd_GivesTheOtherEnd()
    {
        var pair = ChannelPair.Of(High, Low);

        Assert.Equal(High, pair.Other(Low));
        Assert.Equal(Low, pair.Other(High));
    }
}
