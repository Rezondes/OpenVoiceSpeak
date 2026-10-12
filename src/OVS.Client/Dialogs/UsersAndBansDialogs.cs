using OVS.Client.ViewModels;

namespace OVS.Client.Dialogs;

/// <summary>The dialogs of the users and bans tabs. Unset entries mean "cancelled" (used by tests).</summary>
public sealed record UsersAndBansDialogs
{
    /// <summary>Nickname, and whether an IP is known to ban as well (Package 72: not for every offline user).</summary>
    public Func<string, bool, Func<BanChoice, PendingViewModel>?, Task<BanChoice?>>? Ban { get; init; }
    /// <summary>Package 72: the red "Alle Daten von {0} löschen?" dialog.</summary>
    public Func<string, Task<bool>>? ConfirmDeleteUser { get; init; }
}
