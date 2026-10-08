using Avalonia.Controls;
using OVS.Client.Controls;
using OVS.Client.Localization;
using OVS.Client.ViewModels;
using static OVS.Client.Dialogs.DialogControls;

namespace OVS.Client.Dialogs;

/// <summary>
/// Package 133 (A142): "Verbindungsdetails", bound to the figures, so the open card follows their refresh every 2 s.
/// Only a close button: there is nothing to decide.
/// </summary>
internal static class ConnectionDetailsDialog
{
    internal static async Task ShowAsync(OverlayHost overlay, ConnectionStatsViewModel stats)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 16, RowSpacing = 6, DataContext = stats };
        (string Label, string Property)[] rows =
        [
            (Strings.ConnDetails_RoundTrip, nameof(ConnectionStatsViewModel.RoundTripText)),
            (Strings.ConnDetails_Uplink, nameof(ConnectionStatsViewModel.UplinkText)),
            (Strings.ConnDetails_Downlink, nameof(ConnectionStatsViewModel.DownlinkText)),
            (Strings.ConnDetails_Jitter, nameof(ConnectionStatsViewModel.JitterText)),
            (Strings.ConnDetails_Buffer, nameof(ConnectionStatsViewModel.DelayText)),
            (Strings.ConnDetails_Concealed, nameof(ConnectionStatsViewModel.ConcealedText)),
            (Strings.ConnDetails_Bitrate, nameof(ConnectionStatsViewModel.BitrateText)),
        ];
        foreach (var (label, property) in rows)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var name = CreateText(label, "muted");
            var value = CreateText("");
            value.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(property));
            Grid.SetRow(name, grid.RowDefinitions.Count - 1);
            Grid.SetRow(value, grid.RowDefinitions.Count - 1);
            Grid.SetColumn(value, 1);
            grid.Children.AddRange([name, value]);
        }
        await DialogFrame.ShowAsync(overlay, Strings.ConnDetails_Title, "WifiSignal", CreateStack(grid, CreateText(Strings.ConnDetails_Hint, "caption")), () => "ok",
            new(Strings.Ui_Close, WithCancel: false));
    }
}
