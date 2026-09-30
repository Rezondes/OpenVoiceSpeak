using OVS.Client.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using OVS.Shared.Protocol;

namespace OVS.Client.ViewModels;

/// <summary>Package 93: one group of the "Nur für Gruppen" list.</summary>
public sealed partial class GroupChoice(Guid id, string name, bool isChecked) : ObservableObject
{
    public Guid Id { get; } = id;
    public string Name { get; } = name;
    [ObservableProperty] bool isChecked = isChecked;
}

/// <summary>
/// Package 54: one dialog for a channel with every option. Creating opens it empty, editing with the channel's data;
/// only title and button differ. The default channel stays unlimited and open, so its slots and group lock are locked.
/// </summary>
public sealed partial class ChannelDialogViewModel(ChannelEdit current, ChannelDialogMode mode, IReadOnlyList<GroupInfo>? groups = null) : ObservableObject
{
    [ObservableProperty] string name = current.Name;
    [ObservableProperty] string description = current.Description;
    [ObservableProperty] bool isMuted = current.IsMuted;
    [ObservableProperty] decimal? maxUsers = current.MaxUsers;
    /// <summary>Package 83: why the entered channel would be refused by the server, null while it is fine.</summary>
    [ObservableProperty] string? error;

    public string Title => mode == ChannelDialogMode.Create ? Strings.Dialog_CreateChannel : Strings.Dialog_EditChannel;
    public string ConfirmText => mode == ChannelDialogMode.Create ? Strings.Dlg_Create : Strings.Dlg_Save;
    public bool CanLimitUsers => mode != ChannelDialogMode.EditDefault;
    public string SlotsHint => CanLimitUsers ? Strings.Dlg_MaxUsersHint : Strings.Dlg_MaxUsersDefault;
    public decimal MaxSlots => ProtocolInfo.MaxChannelUsers;

    /// <summary>Package 93: every group of the server, in its order, ticked when in the channel's lock.</summary>
    public IReadOnlyList<GroupChoice> Groups { get; } =
        (groups ?? []).Select(g => new GroupChoice(g.Id, g.Name, current.AllowedGroupIds?.Contains(g.Id) == true)).ToList();
    public bool CanLockGroups => mode != ChannelDialogMode.EditDefault;
    public string GroupLockHint => CanLockGroups ? Strings.Dlg_GroupLockHint : Strings.Dlg_GroupLockDefault;

    /// <summary>Package 94: a new password; empty leaves it as it is.</summary>
    [ObservableProperty] string password = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEnterPassword))]
    bool removePassword;
    /// <summary>Package 94: the channel has a password, so "Passwort entfernen" is offered.</summary>
    public bool HasPassword => current.HasPassword;
    public bool CanEnterPassword => CanLockGroups && !RemovePassword;

    partial void OnMaxUsersChanged(decimal? value)
    {
        if (value is { } v && (v < 0 || v > MaxSlots)) MaxUsers = Math.Clamp(v, 0, MaxSlots);
    }

    /// <summary>
    /// The entered channel, or null while name or description break the server's rules (the dialog then stays open).
    /// AllowedGroupIds is null while the ticks are as before (the server keeps the lock, also an "admins only" one).
    /// </summary>
    public ChannelEdit? Result()
    {
        // Package 83: the same TextRules as the server; an empty name stays quiet as before
        Error = string.IsNullOrWhiteSpace(Name) ? null
            : TextRules.Name(Name, ProtocolInfo.MaxNameLength) is null ? Strings.Dlg_NameInvalid
            : TextRules.Text(Description ?? "", ProtocolInfo.MaxTextLength) is null ? Strings.Dlg_TextInvalid
            : (Password ?? "").Length > ProtocolInfo.MaxPasswordLength ? Strings.Dlg_PasswordTooLong
            : null;
        var ticked = Groups.Where(g => g.IsChecked).Select(g => g.Id).ToList();
        bool changed = CanLockGroups && !ticked.ToHashSet().SetEquals(current.AllowedGroupIds ?? []);
        string? password = !CanLockGroups ? null : RemovePassword ? "" : Password is { Length: > 0 } entered ? entered : null;
        return string.IsNullOrWhiteSpace(Name) || Error is not null ? null
            : new ChannelEdit(Name.Trim(), Description ?? "", IsMuted, CanLimitUsers ? (int)(MaxUsers ?? 0) : 0, changed ? ticked : null,
                Password: password);
    }
}
