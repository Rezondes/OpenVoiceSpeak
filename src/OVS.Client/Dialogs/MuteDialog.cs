using Avalonia.Controls;
using Avalonia.Layout;
using OVS.Client.Controls;
using OVS.Client.Localization;
using OVS.Client.ViewModels;
using static OVS.Client.Dialogs.DialogControls;

namespace OVS.Client.Dialogs;

/// <summary>Package 166: how long a server mute lasts, like the ban's duration; "eigene Minuten" shows a field for them.</summary>
internal static class MuteDialog
{
    /// <summary>Asks for the duration of the mute of <paramref name="nickname"/>; null when cancelled.</summary>
    /// <param name="overlay">The overlay the dialog shows in.</param>
    /// <param name="nickname">The user to mute.</param>
    /// <param name="submit">Sends the mute; null closes the dialog with the answer.</param>
    internal static Task<MuteChoice?> ShowAsync(OverlayHost overlay, string nickname, Func<MuteChoice, PendingActionViewModel>? submit = null)
    {
        var durations = MuteChoice.Durations;
        var duration = new ComboBox { ItemsSource = durations.Select(choice => choice.Label).ToList(), SelectedIndex = 1, HorizontalAlignment = HorizontalAlignment.Stretch };
        var minutes = new NumericUpDown { Minimum = 1, Maximum = MuteChoice.MaxMinutes, Value = 30, Increment = 5, FormatString = "0", HorizontalAlignment = HorizontalAlignment.Stretch };
        var ownField = CreateField(Strings.Mute_Minutes, minutes);
        ownField.IsVisible = false;
        duration.SelectionChanged += (_, _) => ownField.IsVisible = durations[Math.Max(0, duration.SelectedIndex)].Value == MuteChoice.OwnMinutes;
        var body = CreateStack(CreateField(Strings.Dlg_Duration, duration), ownField, CreateText(Strings.Mute_Hint, "caption"));
        return DialogFrame.ShowAsync(overlay, TextFormat.Of(Strings.Mute_Title, nickname), "SpeakerMute", body, () =>
            durations[Math.Max(0, duration.SelectedIndex)].Value is var chosen && chosen == MuteChoice.OwnMinutes
                ? minutes.Value is { } own ? new MuteChoice((int)own) : null
                : new MuteChoice(chosen),
            new(Strings.Mute_Confirm, Kind: DialogKind.Danger, Submit: submit));
    }
}
