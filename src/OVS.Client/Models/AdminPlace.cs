using OVS.Client.ViewModels;

namespace OVS.Client.Models;

/// <summary>
/// A place of the administration page that needs a manual save (A122), carried by its tab: whether it holds changes,
/// saving them (waits for the server; false when it refused, did not answer or the input is invalid) and throwing them away.
/// </summary>
internal sealed record AdminPlace(Func<bool> HasChanges, Func<Task<bool>> SaveAsync, Action Discard)
{
    /// <summary>
    /// A place whose save waits for one already on its way first (it may be the one the user means), then sends only
    /// when there is a change and waits for the server's answer; send gives null when the input is invalid.
    /// </summary>
    internal static AdminPlace Create(Func<bool> hasChanges, PendingActionViewModel? pendingSave, Func<PendingActionViewModel?> send, Action discard) =>
        Create(hasChanges, pendingSave, async () => send() is { } sent && await sent.Completion is null, discard);

    /// <summary>
    /// A place with several changed parts: like the other overload, but <paramref name="sendAll"/> saves every changed
    /// part and gives true only when all are confirmed.
    /// </summary>
    internal static AdminPlace Create(Func<bool> hasChanges, PendingActionViewModel? pendingSave, Func<Task<bool>> sendAll, Action discard) =>
        new(hasChanges, async () =>
        {
            if (pendingSave is { IsRunning: true }) await pendingSave.Completion;
            if (!hasChanges()) return true;
            return await sendAll();
        }, discard);
}
