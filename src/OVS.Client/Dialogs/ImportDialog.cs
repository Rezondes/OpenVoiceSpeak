using Avalonia.Controls;
using OVS.Client.Controls;
using OVS.Client.Localization;
using OVS.Client.Models;
using static OVS.Client.Dialogs.DialogControls;

namespace OVS.Client.Dialogs;

/// <summary>
/// Package 171: "Aus Discord übernehmen": one row per change with the value now and the new one, every row checked; the
/// checked ones are taken over. Space toggles the focused row, Enter takes over (DialogFrame). With nothing to change
/// it says so, and the notes (a device not found) show below.
/// </summary>
internal static class ImportDialog
{
    /// <summary>The checked changes; null when cancelled or when there was nothing to change.</summary>
    internal static async Task<IReadOnlyList<ImportChange>?> ShowAsync(OverlayHost overlay, ImportPlan plan)
    {
        var title = TextFormat.Of(Strings.Import_TitleFmt, plan.Source);
        var notes = plan.Notes.Select(note => (Control)CreateText(note, "caption")).ToList();
        if (plan.Changes.Count == 0)
        {
            await DialogFrame.ShowAsync(overlay, title, "ArrowDown", CreateStack([CreateText(Strings.Import_Nothing), .. notes]), () => "ok", new(WithCancel: false));
            return null;
        }
        var rows = plan.Changes.Select(change => new CheckBox
        {
            IsChecked = true,
            Tag = change,
            Content = new StackPanel
            {
                Spacing = 2,
                Children = { CreateText(change.Name), CreateText(TextFormat.Of(Strings.Import_BeforeAfterFmt, change.Before, change.After), "caption") },
            },
        }).ToList();
        var body = CreateStack([CreateText(Strings.Import_Intro, "caption"), .. rows, .. notes]);
        return await DialogFrame.ShowAsync<List<ImportChange>>(overlay, title, "ArrowDown", body,
            () => rows.Where(row => row.IsChecked == true).Select(row => (ImportChange)row.Tag!).ToList(), new(Strings.Import_Apply));
    }
}
