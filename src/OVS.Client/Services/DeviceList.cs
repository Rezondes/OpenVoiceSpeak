using OVS.Client.Audio.Devices;

namespace OVS.Client.Services;

/// <summary>
/// Package 52: the audio devices the settings page offers. Reading a device's name takes 30 to 50 ms, with many (virtual)
/// devices together more than half a second. So the list is loaded in the background, the last one known is kept for a
/// settings page that opens before the next one is in, and a loaded list is handed to the page through a delegate.
/// </summary>
internal sealed class DeviceList
{
    readonly Action<Action> post;
    readonly Func<AudioDirection, List<AudioDevice>> listDevices;
    readonly Action<IReadOnlyList<AudioDevice>, IReadOnlyList<AudioDevice>> showDevices;
    int loads;

    /// <param name="post">Runs an action on the UI thread.</param>
    /// <param name="listDevices">The active devices of a direction; null: none, an empty list.</param>
    /// <param name="showDevices">Shows a loaded list (inputs, outputs) on the settings page, if it is open.</param>
    internal DeviceList(Action<Action> post, Func<AudioDirection, List<AudioDevice>>? listDevices,
        Action<IReadOnlyList<AudioDevice>, IReadOnlyList<AudioDevice>> showDevices)
    {
        this.post = post;
        this.listDevices = listDevices ?? (_ => []);
        this.showDevices = showDevices;
    }

    /// <summary>The last list loaded; null before the first.</summary>
    internal ActiveDevices? Known { get; private set; }

    /// <summary>
    /// Loads both lists on the pool and shows them on the UI thread, unless a newer load was started meanwhile. Returns the
    /// load's last step, which ends cancelled when the listing failed; callers in the app ignore it, tests await it.
    /// </summary>
    internal Task RefreshAsync()
    {
        int load = ++loads;
        return Task.Run(() => new ActiveDevices(listDevices(AudioDirection.Input), listDevices(AudioDirection.Output)))
            .ContinueWith(listing => post(() =>
            {
                if (load != loads) return; // a newer list is on its way
                Known = listing.Result;
                showDevices(listing.Result.Inputs, listing.Result.Outputs);
            }), CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }
}
