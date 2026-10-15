using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OVS.Client.Dialogs;
using OVS.Client.Localization;
using OVS.Client.Models;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Dialogs;

/// <summary>Package 171: the import dialog shows every change checked; the user unchecks what stays.</summary>
[Collection(MotionCollection.Name)]
public sealed class ImportDialogTests
{
    static ImportPlan Plan(params string[] names) =>
        new("Discord", names.Select(name => new ImportChange(name, "Aus", "An", _ => { })).ToList(), []);

    static List<CheckBox> Rows(OverlayScene scene) => scene.Overlay.GetVisualDescendants().OfType<CheckBox>().ToList();

    [AvaloniaFact]
    public void Rows_Shown_AllCheckedByDefault()
    {
        var scene = OverlayRig.Open();
        try
        {
            _ = ImportDialog.ShowAsync(scene.Overlay, Plan(Strings.Ui_AutoGain, Strings.Ui_NoiseSuppression) with { Notes = ["Mikrofon: Gerät nicht gefunden"] });
            Dispatcher.UIThread.RunJobs();

            Assert.Equal([true, true], Rows(scene).Select(row => row.IsChecked == true));
            var texts = scene.Overlay.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToList();
            Assert.Contains(TextFormat.Of(Strings.Import_TitleFmt, "Discord"), texts);
            Assert.Contains(TextFormat.Of(Strings.Import_BeforeAfterFmt, "Aus", "An"), texts);
            Assert.Contains("Mikrofon: Gerät nicht gefunden", texts);
        }
        finally
        {
            scene.Window.Close();
        }
    }

    [AvaloniaFact]
    public void Uncheck_Row_ThatSettingKept()
    {
        var scene = OverlayRig.Open();
        try
        {
            var shown = ImportDialog.ShowAsync(scene.Overlay, Plan(Strings.Ui_AutoGain, Strings.Ui_NoiseSuppression));
            Dispatcher.UIThread.RunJobs();
            Rows(scene)[0].IsChecked = false;
            OverlayRig.Click(OverlayRig.FindMainButton(scene.Overlay));

            Assert.Equal([Strings.Ui_NoiseSuppression], shown.Result!.Select(change => change.Name));
        }
        finally
        {
            scene.Window.Close();
        }
    }

    [AvaloniaFact]
    public void Nothing_ToChange_SaysSo()
    {
        var scene = OverlayRig.Open();
        try
        {
            var shown = ImportDialog.ShowAsync(scene.Overlay, Plan());
            Dispatcher.UIThread.RunJobs();

            Assert.Contains(Strings.Import_Nothing, scene.Overlay.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text));
            Assert.Empty(Rows(scene));
            Assert.Single(OverlayRig.FindButtons(scene.Overlay)); // only OK
            OverlayRig.Click(OverlayRig.FindMainButton(scene.Overlay));
            Assert.Null(shown.Result);
        }
        finally
        {
            scene.Window.Close();
        }
    }

    [AvaloniaFact]
    public void Keyboard_SpaceAndEnter_ToggleAndTakeOver()
    {
        var scene = OverlayRig.Open();
        try
        {
            var shown = ImportDialog.ShowAsync(scene.Overlay, Plan(Strings.Ui_AutoGain, Strings.Ui_NoiseSuppression));
            Dispatcher.UIThread.RunJobs();
            Rows(scene)[1].Focus();
            scene.Window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            scene.Window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Dispatcher.UIThread.RunJobs();
            Assert.False(Rows(scene)[1].IsChecked);

            scene.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs();

            Assert.True(shown.IsCompleted);
            Assert.Equal([Strings.Ui_AutoGain], shown.Result!.Select(change => change.Name));
        }
        finally
        {
            scene.Window.Close();
        }
    }
}
