using OVS.Shared.Voice;

namespace OVS.Tests.Shared.Voice;

public class ReplayWindowTests
{
    [Fact]
    public void NewSeq_Accepted()
    {
        var replayWindow = new ReplayWindow();
        Assert.True(replayWindow.Accept(0));
        Assert.True(replayWindow.Accept(1));
        Assert.True(replayWindow.Accept(5));
    }

    [Fact]
    public void Duplicate_Rejected()
    {
        var replayWindow = new ReplayWindow();
        Assert.True(replayWindow.Accept(10));
        Assert.False(replayWindow.Accept(10));
    }

    [Fact]
    public void UpTo63Behind_AcceptedOnce()
    {
        var replayWindow = new ReplayWindow();
        Assert.True(replayWindow.Accept(100));
        Assert.True(replayWindow.Accept(37));
        Assert.False(replayWindow.Accept(37));
        Assert.True(replayWindow.Accept(99));
    }

    [Fact]
    public void Older64_Rejected()
    {
        var replayWindow = new ReplayWindow();
        Assert.True(replayWindow.Accept(100));
        Assert.False(replayWindow.Accept(36));
    }

    [Fact]
    public void BigJump_ShiftsWindow()
    {
        var replayWindow = new ReplayWindow();
        Assert.True(replayWindow.Accept(1));
        Assert.True(replayWindow.Accept(1000));
        Assert.False(replayWindow.Accept(1));
        Assert.True(replayWindow.Accept(999));
        Assert.False(replayWindow.Accept(1000));
    }
}
