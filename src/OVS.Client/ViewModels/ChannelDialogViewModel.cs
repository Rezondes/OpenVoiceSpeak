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

    public string Title => mode == ChannelDialogMode.Create ? Strings.Dialog_CreateChannel : Strings.Dialog_EditChannel;
    public string ConfirmText => mode == ChannelDialogMode.Create ? Strings.Dlg_Create : Strings.Dlg_Save;
    public bool CanLimitUsers => mode != ChannelDialogMode.EditDefault;
    public string SlotsHint => CanLimitUsers ? Strings.Dlg_MaxUsersHint : Strings.Dlg_MaxUsersDefault;
    public decimal MaxSlots => ProtocolInfo.MaxChannelUsers;

    partial void OnMaxUsersChanged(decimal? value)
    {
        if (value is { } v && (v < 0 || v > MaxSlots)) MaxUsers = Math.Clamp(v, 0, MaxSlots);
    }

    /// <summary>The entered channel, or null while the name is missing (the dialog then stays open).</summary>
    public ChannelEdit? Result() =>
        string.IsNullOrWhiteSpace(Name) ? null
        : new ChannelEdit(Name.Trim(), Description ?? "", IsMuted, CanLimitUsers ? (int)(MaxUsers ?? 0) : 0);
}
