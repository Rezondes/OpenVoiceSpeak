using OVS.Client.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using OVS.Shared.Protocol;

namespace OVS.Client.ViewModels;

/// <summary>
/// Package 54: one dialog for a channel with every option. Creating opens it empty, editing with the channel's data;
/// only title and button differ. The default channel stays unlimited, so its slots are locked.
/// </summary>
public sealed partial class ChannelDialogViewModel(ChannelEdit current, ChannelDialogMode mode) : ObservableObject
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

    partial void OnMaxUsersChanged(decimal? value)
    {
        if (value is { } v && (v < 0 || v > MaxSlots)) MaxUsers = Math.Clamp(v, 0, MaxSlots);
    }

    /// <summary>The entered channel, or null while name or description break the server's rules (the dialog then stays open).</summary>
    public ChannelEdit? Result()
    {
        // Package 83: the same TextRules as the server; an empty name stays quiet as before
        Error = string.IsNullOrWhiteSpace(Name) ? null
            : TextRules.Name(Name, ProtocolInfo.MaxNameLength) is null ? Strings.Dlg_NameInvalid
            : TextRules.Text(Description ?? "", ProtocolInfo.MaxTextLength) is null ? Strings.Dlg_TextInvalid
            : null;
        return string.IsNullOrWhiteSpace(Name) || Error is not null ? null
            : new ChannelEdit(Name.Trim(), Description ?? "", IsMuted, CanLimitUsers ? (int)(MaxUsers ?? 0) : 0);
    }
}
