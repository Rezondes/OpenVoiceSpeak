using OVS.Client.Input;
using OVS.Client.Net.Updates;
using OVS.Client.Settings;
using OVS.Client.ViewModels;

namespace OVS.Client.Dialogs;

/// <summary>Dialogs the view provides. Unset entries mean "cancelled" (used by tests).</summary>
public sealed class DialogCallbacks
{
    /// <summary>Package 114: unsaved changes would be lost; unset means leave without saving (as before).</summary>
    public Func<Task<LeaveChoice>>? AskLeave { get; init; }
    /// <summary>Package 54: one dialog for creating (empty) and editing (prefilled); the mode sets title and button.</summary>
    /// <remarks>
    /// Package 98: the dialogs that send get the send as their last argument. It returns the waiting mark; the dialog keeps
    /// its card open with a busy button until the mark ends, closes when confirmed and shows the error otherwise. A dialog
    /// that never calls it (a test fake) has the answer sent after it closed.
    /// </remarks>
    public Func<ChannelEdit, ChannelDialogMode, Func<ChannelEdit, PendingViewModel>?, Task<ChannelEdit?>>? EditChannel { get; init; }
    public Func<string, IReadOnlyList<ChannelViewModel>, Func<ChannelViewModel, PendingViewModel>?, Task<ChannelViewModel?>>? PickChannel { get; init; }
    public Func<string, string, Func<string, PendingViewModel>?, Task<string?>>? AskText { get; init; }
    /// <summary>Nickname, and whether an IP is known to ban as well (Package 72: not for every offline user).</summary>
    public Func<string, bool, Func<BanChoice, PendingViewModel>?, Task<BanChoice?>>? Ban { get; init; }
    public Func<string, Task<bool>>? Confirm { get; init; }
    /// <summary>Package 72: the red "Alle Daten von {0} löschen?" dialog.</summary>
    public Func<string, Task<bool>>? ConfirmDeleteUser { get; init; }
    /// <summary>Package 74: the red question before a backup replaces the server's state (argument: the backup's title).</summary>
    public Func<string, Task<bool>>? ConfirmRestore { get; init; }
    /// <summary>Package 89 (A101): the warning before a download, the file holds the server's private key and user data.</summary>
    public Func<Task<bool>>? ConfirmBackupDownload { get; init; }
    /// <summary>Package 40: the server wants a password (none stored or the stored one is wrong).</summary>
    public Func<string, Task<PasswordAnswer?>>? AskPassword { get; init; }
    /// <summary>Package 94: the password of a channel (argument: its name); null = cancelled.</summary>
    public Func<string, Task<string?>>? AskChannelPassword { get; init; }
    public Func<Bookmark, Task<BookmarkEdit?>>? EditBookmark { get; init; }
    /// <summary>Package 41: action and key; null binding = add. The second argument captures the next key.</summary>
    public Func<KeyBinding?, Func<KeyAction, Task<KeyChord?>>, Task<KeyBinding?>>? EditKeyBinding { get; init; }
    /// <summary>Package 43: "Version X ist verfügbar. Jetzt installieren?"</summary>
    public Func<UpdateOffer, Task<bool>>? OfferUpdate { get; init; }
    /// <summary>Package 133: the "Verbindungsdetails" card; it shows the figures as they are refreshed until closed.</summary>
    public Func<ConnectionStatsViewModel, Task>? ConnectionDetails { get; init; }
}
