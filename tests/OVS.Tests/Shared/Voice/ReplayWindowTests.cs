using OVS.Shared.Voice;

namespace OVS.Tests.Shared.Voice;

public class ReplayWindowTests
{
    [Fact]
    public void Accept_NewNumber_Accepted()
    {
        var replayWindow = new ReplayWindow();
        Assert.True(replayWindow.Accept(0));
        Assert.True(replayWindow.Accept(1));
        Assert.True(replayWindow.Accept(5));
    }

    [Fact]
    public void Accept_Duplicate_Rejected()
    {
        var replayWindow = new ReplayWindow();
        Assert.True(replayWindow.Accept(10));
        Assert.False(replayWindow.Accept(10));
    }

    [Fact]
    public void Accept_UpTo63Behind_AcceptedOnce()
    {
        var replayWindow = new ReplayWindow();
        Assert.True(replayWindow.Accept(100));
        Assert.True(replayWindow.Accept(37));
        Assert.False(replayWindow.Accept(37));
        Assert.True(replayWindow.Accept(99));
    }

    [Fact]
    public void Accept_64Behind_Rejected()
    {
        var replayWindow = new ReplayWindow();
        Assert.True(replayWindow.Accept(100));
        Assert.False(replayWindow.Accept(36));
    }

    [Fact]
    public void Accept_BigJump_ShiftsWindow()
    {
        var replayWindow = new ReplayWindow();
        Assert.True(replayWindow.Accept(1));
        Assert.True(replayWindow.Accept(1000));
        Assert.False(replayWindow.Accept(1));
        Assert.True(replayWindow.Accept(999));
        Assert.False(replayWindow.Accept(1000));
    }
}
