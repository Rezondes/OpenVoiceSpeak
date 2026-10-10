using OVS.Client.Audio.Devices;
using OVS.Client.Services;
using OVS.Tests.TestSupport;
using static OVS.Tests.TestSupport.FakeAudioHardware;

namespace OVS.Tests.Client.Services;

/// <summary>
/// Package 52: loading the audio devices the settings page offers in the background, showing the list on the UI thread,
/// keeping the last one known, and dropping an older load when a newer one started. The devices are fakes, the UI thread is
/// a <see cref="PostQueue"/>.
/// </summary>
public sealed class DeviceListTests
{
    readonly ManualTimeProvider clock = new();
    readonly PostQueue posts = new();
    readonly List<(IReadOnlyList<AudioDevice> Inputs, IReadOnlyList<AudioDevice> Outputs)> shownLists = [];

    DeviceList CreateList(Func<AudioDirection, List<AudioDevice>>? listDevices = null) =>
        new(posts.Post, listDevices, (inputs, outputs) => shownLists.Add((inputs, outputs)));

    [Fact]
    public async Task Refresh_BothDirectionsListed_ShowsThemOnceOnTheUiThreadAndRemembersThem()
    {
        var list = CreateList(direction => direction == AudioDirection.Input ? [Headset, Webcam] : [Speakers, MonitorOutput]);

        _ = list.Refresh();
        await posts.RunAsync(clock, TimeSpan.Zero);

        var (shownInputs, shownOutputs) = Assert.Single(shownLists);
        Assert.Equal<AudioDevice>([Headset, Webcam], shownInputs);
        Assert.Equal<AudioDevice>([Speakers, MonitorOutput], shownOutputs);
        Assert.Equal<AudioDevice>([Headset, Webcam], list.Known!.Inputs);
        Assert.Equal<AudioDevice>([Speakers, MonitorOutput], list.Known.Outputs);
    }

    [Fact]
    public void Known_NothingLoadedYet_IsNull()
    {
        var list = CreateList();

        Assert.Null(list.Known);
    }

    [Fact]
    public async Task Refresh_NoListing_ShowsEmptyLists()
    {
        var list = CreateList();

        _ = list.Refresh();
        await posts.RunAsync(clock, TimeSpan.Zero);

        var (shownInputs, shownOutputs) = Assert.Single(shownLists);
        Assert.Equal((0, 0), (shownInputs.Count, shownOutputs.Count));
    }

    [Fact]
    public async Task Refresh_ListingWaits_ReturnsBeforeTheListingIsDone()
    {
        using var release = new ManualResetEventSlim();
        bool listingDone = false;
        var list = CreateList(direction =>
        {
            release.Wait(HangGuard.Limit);
            listingDone = true;
            return [];
        });

        _ = list.Refresh();

        Assert.False(Volatile.Read(ref listingDone));
        release.Set();
        await posts.RunAsync(clock, TimeSpan.Zero);
    }

    [Fact]
    public async Task Refresh_ListingThrows_PostsNothingAndRemembersNothing()
    {
        int postCalls = 0;
        var list = new DeviceList(_ => Interlocked.Increment(ref postCalls), direction =>
        {
            throw new InvalidOperationException("device list unavailable");
        }, (inputs, outputs) => shownLists.Add((inputs, outputs)));

        await HangGuard.Bound(list.Refresh().ContinueWith(_ => { }, TaskScheduler.Default)); // the load ends cancelled when the listing failed

        Assert.Equal(0, Volatile.Read(ref postCalls));
        Assert.Empty(shownLists);
        Assert.Null(list.Known);
    }

    [Fact]
    public async Task Refresh_NewerLoadStarted_DropsTheOlderList()
    {
        var firstListingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseFirstListing = new ManualResetEventSlim();
        int listings = 0;
        var list = CreateList(direction =>
        {
            if (Interlocked.Increment(ref listings) == 1)
            {
                firstListingStarted.SetResult();
                releaseFirstListing.Wait(HangGuard.Limit); // the older load is slow
                return [Headset];
            }
            return direction == AudioDirection.Input ? [Webcam] : [MonitorOutput];
        });
        _ = list.Refresh();
        await HangGuard.Bound(firstListingStarted.Task);
        _ = list.Refresh();
        await posts.RunAsync(clock, TimeSpan.Zero); // the newer list is shown

        releaseFirstListing.Set();
        await posts.RunAsync(clock, TimeSpan.Zero); // the older one arrives late

        Assert.Equal<AudioDevice>([Webcam], Assert.Single(shownLists).Inputs);
    }
}
