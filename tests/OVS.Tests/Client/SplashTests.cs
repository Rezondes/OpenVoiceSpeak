using OVS.Client;

namespace OVS.Tests.Client;

/// <summary>The splash shows the client's logo and goes once it is closed, also when closed before its window exists.</summary>
public sealed class SplashTests
{
    static readonly string Exe = Path.Combine(AppContext.BaseDirectory, "OVS.Client.exe"); // the test host has no icon

    [Fact]
    public void Shows_ThenCloseEndsIt()
    {
        var shown = Splash.Show(Exe);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!Splash.IsShown && watch.ElapsedMilliseconds < 2000) Thread.Sleep(5);
        Assert.True(Splash.IsShown);
        Splash.Close();
        Assert.True(shown.Join(TimeSpan.FromSeconds(2)), "it fades out and ends");
        Assert.False(Splash.IsShown);

        var early = Splash.Show(Exe);
        Splash.Close();
        Assert.True(early.Join(TimeSpan.FromSeconds(2)));
    }
}
