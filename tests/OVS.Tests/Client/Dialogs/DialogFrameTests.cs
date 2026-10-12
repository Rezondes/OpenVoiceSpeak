using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OVS.Client.Controls;
using OVS.Client.Dialogs;
using OVS.Client.Localization;
using OVS.Client.ViewModels;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Dialogs;

/// <summary>The one frame of every dialog: what its options make of the buttons, Enter and the send (Package 98, 114, 133).</summary>
public sealed class DialogFrameTests
{
    static Task<string?> Show(OverlayHost overlay, DialogOptions<string>? options = null, Func<string?>? accept = null)
    {
        var shown = DialogFrame.ShowAsync(overlay, "Titel", "Delete", new TextBlock { Text = "Inhalt" }, accept ?? (() => "ok"), options);
        Dispatcher.UIThread.RunJobs();
        return shown;
    }

    [AvaloniaFact]
    public void ShowAsync_NoOptions_CancelThenAccentOk()
    {
        var (window, overlay) = OverlayRig.Open();
        try
        {
            _ = Show(overlay);
            var buttons = OverlayRig.FindButtons(overlay);
            Assert.Equal([Strings.Dlg_Cancel, "OK"], buttons.Select(OverlayRig.ReadText));
            Assert.Contains("accent", buttons[1].Classes);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ShowAsync_OkAndCancelText_NameTheButtons()
    {
        var (window, overlay) = OverlayRig.Open();
        try
        {
            _ = Show(overlay, new("Speichern", CancelText: "Später"));
            Assert.Equal(["Später", "Speichern"], OverlayRig.FindButtons(overlay).Select(OverlayRig.ReadText));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ShowAsync_DangerKind_OkIsRed()
    {
        var (window, overlay) = OverlayRig.Open();
        try
        {
            _ = Show(overlay, new("Weg", Kind: DialogKind.Danger));
            var ok = OverlayRig.FindMainButton(overlay);
            Assert.Equal("Weg", OverlayRig.ReadText(ok));
            Assert.Contains("danger", ok.Classes);
            Assert.DoesNotContain("accent", ok.Classes);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ShowAsync_WithoutCancel_OnlyTheOkButtonWhichAnswers()
    {
        var (window, overlay) = OverlayRig.Open();
        try
        {
            var shown = Show(overlay, new("Schließen", WithCancel: false));
            var buttons = OverlayRig.FindButtons(overlay);
            Assert.Equal(["Schließen"], buttons.Select(OverlayRig.ReadText));
            OverlayRig.Click(buttons[0]);
            Assert.Equal("ok", await shown);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ShowAsync_ThirdAnswer_ThreeButtonsDefaultFirstAndItsValueReturned()
    {
        var (window, overlay) = OverlayRig.Open();
        try
        {
            var shown = Show(overlay, new("Speichern", CancelText: "Bleiben", ThirdAnswer: ("Verwerfen", "discard")));
            var buttons = OverlayRig.FindButtons(overlay);
            Assert.Equal(["Speichern", "Verwerfen", "Bleiben"], buttons.Select(OverlayRig.ReadText));
            OverlayRig.Click(buttons[1]);
            Assert.Equal("discard", await shown);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(true, "ok")]
    [InlineData(false, null)]
    public async Task ShowAsync_EnterPressed_AcceptsOnlyWhenOkIsDefault(bool okIsDefault, string? expected)
    {
        var (window, overlay) = OverlayRig.Open();
        try
        {
            var shown = Show(overlay, new(OkIsDefault: okIsDefault));
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(shown.IsCompleted);
            Assert.Equal(expected, await shown);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ShowAsync_AcceptGivesNothing_StaysOpen()
    {
        var (window, overlay) = OverlayRig.Open();
        try
        {
            var shown = Show(overlay, accept: () => null);
            OverlayRig.Click(OverlayRig.FindMainButton(overlay));
            Assert.False(shown.IsCompleted);
            Assert.True(overlay.IsOpen);
            overlay.Close();
            Dispatcher.UIThread.RunJobs();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ShowAsync_SubmitRefusedThenConfirmed_ShowsTheErrorThenCloses()
    {
        var (window, overlay) = OverlayRig.Open();
        try
        {
            var sending = new PendingActionViewModel(new ManualTimeProvider());
            var sent = new List<string>();
            var shown = Show(overlay, new(Submit: answer =>
            {
                sent.Add(answer);
                sending.Start();
                return sending;
            }));
            var ok = OverlayRig.FindMainButton(overlay);
            OverlayRig.Click(ok);
            Assert.Equal(["ok"], sent);
            Assert.True(overlay.IsOpen); // waits for the server
            sending.Fail("abgelehnt");
            Dispatcher.UIThread.RunJobs();
            Assert.True(overlay.IsOpen);
            Assert.Contains(overlay.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "abgelehnt" && t.IsVisible);
            OverlayRig.Click(ok); // sent again
            sending.Done();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(["ok", "ok"], sent);
            Assert.Equal("ok", await shown);
        }
        finally
        {
            window.Close();
        }
    }
}
