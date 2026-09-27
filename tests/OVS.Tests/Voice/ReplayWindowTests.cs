using OVS.Shared.Voice;

namespace OVS.Tests.Voice;

public class ReplayWindowTests
{
    [Fact]
    public void NewSeq_Accepted()
    {
        var w = new ReplayWindow();
        Assert.True(w.Accept(0));
        Assert.True(w.Accept(1));
        Assert.True(w.Accept(5));
    }

    [Fact]
    public void Duplicate_Rejected()
    {
        var w = new ReplayWindow();
        Assert.True(w.Accept(10));
        Assert.False(w.Accept(10));
    }

    [Fact]
    public void UpTo63Behind_AcceptedOnce()
    {
        var w = new ReplayWindow();
        Assert.True(w.Accept(100));
        Assert.True(w.Accept(37));
        Assert.False(w.Accept(37));
        Assert.True(w.Accept(99));
    }

    [Fact]
    public void Older64_Rejected()
    {
        var w = new ReplayWindow();
        Assert.True(w.Accept(100));
        Assert.False(w.Accept(36));
    }

    [Fact]
    public void BigJump_ShiftsWindow()
    {
        var w = new ReplayWindow();
        Assert.True(w.Accept(1));
        Assert.True(w.Accept(1000));
        Assert.False(w.Accept(1));
        Assert.True(w.Accept(999));
        Assert.False(w.Accept(1000));
    }
}
