using Avalonia.Controls;
using Avalonia.Layout;
using OVS.Client.Controls;
using OVS.Client.Localization;
using OVS.Client.ViewModels;
using static OVS.Client.Dialogs.DialogControls;

namespace OVS.Client.Dialogs;

/// <summary>Reason, duration and whether the IP is banned too.</summary>
internal static class BanDialog
{
    /// <param name="ipKnown">Package 72: an offline user without a stored IP cannot be banned by IP.</param>
    internal static Task<BanChoice?> ShowAsync(OverlayHost overlay, string nickname, bool ipKnown = true, Func<BanChoice, PendingActionViewModel>? submit = null)
    {
        var durations = BanChoice.Durations;
        var reason = new TextBox();
        var duration = new ComboBox { ItemsSource = durations.Select(d => d.Label).ToList(), SelectedIndex = 1, HorizontalAlignment = HorizontalAlignment.Stretch };
        var includeIp = new CheckBox { Content = Strings.Dlg_BanIp, IsEnabled = ipKnown };
        var body = CreateStack(CreateField(Strings.Dlg_Reason, reason), CreateField(Strings.Dlg_Duration, duration), includeIp);
        if (!ipKnown) body.Children.Add(CreateText(Strings.Dlg_BanIpUnknown, "caption"));
        return DialogFrame.ShowAsync(overlay, string.Format(Strings.Dlg_BanTitle, nickname), "Prohibited", body,
            () => new BanChoice(reason.Text ?? "", durations[Math.Max(0, duration.SelectedIndex)].Minutes, includeIp.IsChecked == true),
            new(Strings.Dlg_Ban, Kind: DialogKind.Danger, Submit: submit));
    }
}
