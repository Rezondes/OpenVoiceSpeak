using OVS.Client.ViewModels;

namespace OVS.Client.Dialogs;

/// <summary>The dialogs the server view model and its channel and user items ask. Unset entries mean "cancelled" (used by tests).</summary>
public sealed record ServerDialogs
{
    /// <summary>Package 94: the password of a channel (argument: its name); null = cancelled.</summary>
    public Func<string, Task<string?>>? AskChannelPassword { get; init; }
    /// <summary>Package 54: one dialog for creating (empty) and editing (prefilled); the mode sets title and button.</summary>
    /// <remarks>
    /// Package 98: the dialogs that send get the send as their last argument. It returns the waiting mark; the dialog keeps
    /// its card open with a busy button until the mark ends, closes when confirmed and shows the error otherwise. A dialog
    /// that never calls it (a test fake) has the answer sent after it closed.
    /// </remarks>
    public Func<ChannelEdit, ChannelDialogMode, Func<ChannelEdit, PendingActionViewModel>?, Task<ChannelEdit?>>? EditChannel { get; init; }
    public Func<string, IReadOnlyList<ChannelViewModel>, Func<ChannelViewModel, PendingActionViewModel>?, Task<ChannelViewModel?>>? PickChannel { get; init; }
    public Func<string, string, Func<string, PendingActionViewModel>?, Task<string?>>? AskText { get; init; }
    /// <summary>Nickname, and whether an IP is known to ban as well (Package 72: not for every offline user).</summary>
    public Func<string, bool, Func<BanChoice, PendingActionViewModel>?, Task<BanChoice?>>? Ban { get; init; }
    public Func<string, Task<bool>>? Confirm { get; init; }
}
