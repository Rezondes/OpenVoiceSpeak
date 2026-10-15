using OVS.Client.Services;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Services;

/// <summary>Package 170: a second start hands its link to the running client over the profile's pipe.</summary>
public sealed class SingleInstanceTests
{
    [Fact]
    public async Task Second_HandsLinkOver_Ends()
    {
        var pipeName = "OVS-test-" + Guid.NewGuid().ToString("N");
        using var stop = new CancellationTokenSource();
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var listening = SingleInstance.ListenAsync(pipeName, line => received.TrySetResult(line), stop.Token);

        await AsyncWait.UntilAsync(() => SingleInstance.TryHandOver(pipeName, "ovs://gilde.example.org:7000", TimeSpan.FromSeconds(1))); // once the pipe is open
        Assert.Equal("ovs://gilde.example.org:7000", await received.Task.WaitAsync(TimeSpan.FromSeconds(10)));

        await stop.CancelAsync();
        await listening.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void TryHandOver_NobodyListens_False() =>
        Assert.False(SingleInstance.TryHandOver("OVS-test-" + Guid.NewGuid().ToString("N"), "ovs://host:1", TimeSpan.FromMilliseconds(100)));

    [Fact]
    public void PipeName_OtherProfile_Differs() =>
        Assert.NotEqual(SingleInstance.PipeNameFor("profil-a"), SingleInstance.PipeNameFor("profil-b"));
}
