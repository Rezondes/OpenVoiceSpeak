using Avalonia;
using Avalonia.Controls;
using OVS.Client.Controls;
using OVS.Client.Localization;
using OVS.Client.ViewModels;
using static OVS.Client.Dialogs.DialogControls;

namespace OVS.Client.Dialogs;

/// <summary>Picks one channel of a list, such as where to move a user.</summary>
internal static class ChannelPickerDialog
{
    internal static Task<ChannelViewModel?> ShowAsync(OverlayHost overlay, string title, IReadOnlyList<ChannelViewModel> channels,
        Func<ChannelViewModel, PendingViewModel>? submit = null)
    {
        if (channels.Count == 0) return DialogFrame.ShowAsync<ChannelViewModel>(overlay, title, "Speaker", CreateText(Strings.Dlg_NoChannel, "muted"), () => null);
        var list = new ListBox { ItemsSource = channels.Select(c => c.Name).ToList(), MaxHeight = 320, SelectedIndex = 0, CornerRadius = new CornerRadius(8) };
        return DialogFrame.ShowAsync(overlay, title, "Speaker", list, () => list.SelectedIndex >= 0 ? channels[list.SelectedIndex] : null,
            new(Strings.Dlg_Select, Submit: submit));
    }
}
