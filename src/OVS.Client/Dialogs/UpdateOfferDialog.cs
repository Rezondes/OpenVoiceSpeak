using System.Globalization;
using Avalonia.Controls;
using Avalonia.Media;
using OVS.Client.Controls;
using OVS.Client.Localization;
using OVS.Client.Net.Updates;
using static OVS.Client.Dialogs.DialogControls;

namespace OVS.Client.Dialogs;

/// <summary>Package 43: a newer release is out. "Später" asks again at the next start.</summary>
internal static class UpdateOfferDialog
{
    internal static async Task<bool> ShowAsync(OverlayHost overlay, UpdateOffer offer)
    {
        var notes = new TextBlock { Text = offer.Notes.Length > 0 ? offer.Notes : Strings.Dlg_NoNotes, TextWrapping = TextWrapping.Wrap, MaxWidth = 440 };
        notes.Classes.Add("caption");
        var body = CreateStack(
            CreateText(string.Format(CultureInfo.CurrentCulture, Strings.Dlg_UpdateQuestion, offer.Version)),
            new Border { MaxHeight = 200, Child = new ScrollViewer { Content = notes } },
            CreateText(Strings.Dlg_UpdateHint, "caption"));
        return await DialogFrame.ShowAsync(overlay, Strings.Dlg_UpdateTitle, "ArrowSync", body, () => "ok",
            new(Strings.Dlg_InstallNow, CancelText: Strings.Dlg_Later)) is not null;
    }
}
