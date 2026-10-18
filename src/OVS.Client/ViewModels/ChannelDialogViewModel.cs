using OVS.Client.Dialogs;
using OVS.Client.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Groups;

namespace OVS.Client.ViewModels;

/// <summary>
/// Package 54: one dialog for a channel with every option. Creating opens it empty, editing with the channel's data;
/// only title and button differ. The default channel stays unlimited and open, so its slots and group lock are locked.
/// </summary>
internal sealed partial class ChannelDialogViewModel(ChannelEdit current, ChannelDialogMode mode, IReadOnlyList<GroupInfo>? groups = null) : ObservableObject
{
    [ObservableProperty] string name = current.Name;
    [ObservableProperty] string description = current.Description;
    [ObservableProperty] bool isMuted = current.IsMuted;
    [ObservableProperty] decimal? maxUsers = current.MaxUsers;
    /// <summary>Package 111: creating, the type: a voice channel or a separator (which has no fields).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsChannelFields))]
    bool isSeparator;
    /// <summary>The dialog creates a channel, so voice channel or separator can be chosen.</summary>
    public bool CanChooseKind => mode == ChannelDialogMode.Create;
    /// <summary>The name, description and options show (not for a separator).</summary>
    public bool ShowsChannelFields => !IsSeparator;
    /// <summary>Package 83: why the entered channel would be refused by the server, null while it is fine.</summary>
    [ObservableProperty] string? error;

    /// <summary>The dialog's title.</summary>
    public string Title => mode == ChannelDialogMode.Create ? Strings.Dialog_CreateChannel : Strings.Dialog_EditChannel;
    /// <summary>The confirm button's text.</summary>
    public string ConfirmText => mode == ChannelDialogMode.Create ? Strings.Dlg_Create : Strings.Dlg_Save;
    /// <summary>The user limit can be set (not for the default channel).</summary>
    public bool CanLimitUsers => mode != ChannelDialogMode.EditDefault;
    /// <summary>The hint under the user limit.</summary>
    public string SlotsHint => CanLimitUsers ? Strings.Dlg_MaxUsersHint : Strings.Dlg_MaxUsersDefault;
    /// <summary>The highest user limit.</summary>
    #pragma warning disable CA1822 // bound from XAML, which reads instance properties
    public decimal MaxSlots => ProtocolLimits.MaxChannelUsers;
    #pragma warning restore CA1822

    /// <summary>Package 93: every group of the server, in its order, ticked when in the channel's lock.</summary>
    public IReadOnlyList<AllowedGroupViewModel> Groups { get; } =
        (groups ?? []).Select(groupInfo => new AllowedGroupViewModel(groupInfo.Id, groupInfo.Name, current.AllowedGroupIds?.Contains(groupInfo.Id) == true)).ToList();
    /// <summary>The group lock can be set (not for the default channel).</summary>
    public bool CanLockGroups => mode != ChannelDialogMode.EditDefault;
    /// <summary>The hint under the group lock.</summary>
    public string GroupLockHint => CanLockGroups ? Strings.Dlg_GroupLockHint : Strings.Dlg_GroupLockDefault;

    /// <summary>Package 94: a new password; empty leaves it as it is.</summary>
    [ObservableProperty] string password = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEnterPassword))]
    bool removePassword;
    /// <summary>Package 94: the channel has a password, so "Passwort entfernen" is offered.</summary>
    public bool HasPassword => current.HasPassword;
    /// <summary>A password can be typed (not while it is to be removed).</summary>
    public bool CanEnterPassword => CanLockGroups && !RemovePassword;

    partial void OnMaxUsersChanged(decimal? value)
    {
        if (value is { } slots && (slots < 0 || slots > MaxSlots)) MaxUsers = Math.Clamp(slots, 0, MaxSlots);
    }

    /// <summary>Package 132 (A141): "Qualität" in kbps, also for the default channel.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsLowCopyWarning))]
    decimal? bitrate = current.Bitrate;

    /// <summary>Package 184 (A197): above 256 kbps a lost packet is replaced by a lower-rate copy; the dialog says so.</summary>
    public bool ShowsLowCopyWarning => Bitrate > OVS.Shared.Voice.RelayPayload.FullCopyUpToKbps;
    /// <summary>The lowest bitrate in kbps.</summary>
    #pragma warning disable CA1822 // bound from XAML, which reads instance properties
    public decimal MinBitrate => ProtocolLimits.MinBitrate;
    #pragma warning restore CA1822
    /// <summary>The highest bitrate in kbps.</summary>
    #pragma warning disable CA1822 // bound from XAML, which reads instance properties
    public decimal MaxBitrate => ProtocolLimits.MaxBitrate;
    #pragma warning restore CA1822

    /// <summary>Package 184 (review): the label with the limits from the protocol, so it never names old ones.</summary>
    #pragma warning disable CA1822 // bound from XAML, which reads instance properties
    public string BitrateLabel => TextFormat.Of(Strings.Dlg_BitrateFmt, ProtocolLimits.MinBitrate, ProtocolLimits.MaxBitrate);
    /// <summary>Package 184 (review): the hint with the protocol's default.</summary>
    public string BitrateHint => TextFormat.Of(Strings.Dlg_BitrateHintFmt, ProtocolDefaults.Bitrate);
    /// <summary>The arrows' step in kbps.</summary>
    public decimal BitrateStep => Step;
    #pragma warning restore CA1822
    const int Step = 8;

    partial void OnBitrateChanged(decimal? oldValue, decimal? newValue)
    {
        if (newValue is not { } rate) return;
        if (rate < MinBitrate || rate > MaxBitrate)
        {
            Bitrate = Math.Clamp(rate, MinBitrate, MaxBitrate);
            return;
        }
        // Package 184 (review): an arrow step from a value off the grid (510, or one typed) lands on the grid in its direction
        if (oldValue is { } old && old % Step != 0 && Math.Abs(rate - old) == Step)
            Bitrate = rate < old ? Math.Floor(old / Step) * Step : Math.Min(MaxBitrate, Math.Ceiling(old / Step) * Step);
    }

    /// <summary>
    /// The entered channel, or null while name or description break the server's rules (the dialog then stays open).
    /// AllowedGroupIds is null while the ticks are as before (the server keeps the lock, also an "admins only" one).
    /// </summary>
    public ChannelEdit? Result()
    {
        if (IsSeparator && CanChooseKind)
        {
            Error = null;
            return new ChannelEdit("", "", Kind: ChannelKind.Separator);
        }
        // Package 83: the same TextRules as the server; an empty name stays quiet as before
        Error = string.IsNullOrWhiteSpace(Name) ? null
            : TextRules.NormalizeName(Name, ProtocolLimits.MaxNameLength) is null ? Strings.Dlg_NameInvalid
            : TextRules.NormalizeText(Description ?? "", ProtocolLimits.MaxTextLength) is null ? Strings.Dlg_TextInvalid
            : (Password ?? "").Length > ProtocolLimits.MaxPasswordLength ? Strings.Dlg_PasswordTooLong
            : null;
        var ticked = Groups.Where(allowedGroup => allowedGroup.IsChecked).Select(allowedGroup => allowedGroup.Id).ToList();
        bool changed = CanLockGroups && !ticked.ToHashSet().SetEquals(current.AllowedGroupIds ?? []);
        string? password = !CanLockGroups ? null : RemovePassword ? "" : Password is { Length: > 0 } entered ? entered : null;
        return string.IsNullOrWhiteSpace(Name) || Error is not null ? null
            : new ChannelEdit(Name.Trim(), Description ?? "", IsMuted, CanLimitUsers ? (int)(MaxUsers ?? 0) : 0, changed ? ticked : null,
                Password: password, Bitrate: (int)(Bitrate ?? current.Bitrate)); // an emptied field keeps the channel's
    }
}
