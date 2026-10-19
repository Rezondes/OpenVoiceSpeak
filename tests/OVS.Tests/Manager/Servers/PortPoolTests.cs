using OVS.Manager.Servers;

namespace OVS.Tests.Manager.Servers;

/// <summary>Package M7 (D2): every server has one port of 7000 to 7099 for TCP and UDP.</summary>
public class PortPoolTests
{
    [Fact]
    public void Next_SomeTaken_LowestFree()
    {
        Assert.Equal(7000, PortPool.FindFree([]));
        Assert.Equal(7001, PortPool.FindFree([7000, 7002]));
    }

    [Fact]
    public void Check_OutOfRangeOrTaken_Refused()
    {
        Assert.Equal(PortProblem.OutOfRange, PortPool.Check(6999, [7003]));
        Assert.Equal(PortProblem.OutOfRange, PortPool.Check(7100, [7003]));
        Assert.Equal(PortProblem.Taken, PortPool.Check(7003, [7003]));
        Assert.Equal(PortProblem.None, PortPool.Check(7004, [7003]));
    }

    /// <summary>AC2: with 100 servers there is no port left.</summary>
    [Fact]
    public void Next_HundredServers_NoneFree() => Assert.Null(PortPool.FindFree([.. Enumerable.Range(PortPool.First, PortPool.Count)]));
}
