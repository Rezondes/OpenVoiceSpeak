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
    public Func<string, Task<bool>>? Confirm { get; init; }
    /// <summary>Package 40: the server wants a password (none stored or the stored one is wrong).</summary>
    public Func<string, Task<PasswordAnswer?>>? AskPassword { get; init; }
    /// <summary>What each server view model asks (handed over at its attach).</summary>
    public ServerDialogs Server { get; init; } = new();
    /// <summary>What the administration page asks (handed over when it opens).</summary>
    public AdminDialogs Admin { get; init; } = new();
    public Func<Bookmark, Task<BookmarkEdit?>>? EditBookmark { get; init; }
    /// <summary>Package 41: action and key; null binding = add. The second argument captures the next key.</summary>
    public Func<KeyBinding?, Func<KeyAction, Task<KeyChord?>>, Task<KeyBinding?>>? EditKeyBinding { get; init; }
    /// <summary>Package 43: "Version X ist verfügbar. Jetzt installieren?"</summary>
    public Func<UpdateOffer, Task<bool>>? OfferUpdate { get; init; }
    /// <summary>Package 133: the "Verbindungsdetails" card; it shows the figures as they are refreshed until closed.</summary>
    public Func<ConnectionStatsViewModel, Task>? ConnectionDetails { get; init; }
}
