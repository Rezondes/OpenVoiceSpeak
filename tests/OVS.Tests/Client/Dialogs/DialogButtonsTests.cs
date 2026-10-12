using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using OVS.Client.Dialogs;
using OVS.Client.ViewModels;
using OVS.Client.Views;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Dialogs;

/// <summary>
/// Each dialog's answers (the cases of <see cref="DialogCaseTable"/>): its buttons, whether it is red, what Enter does, and that
/// the sending ones send (Package 98). A new dialog type without a case fails <see cref="DialogCaseTable_EveryDialogType_HasACase"/>.
/// </summary>
public sealed class DialogButtonsTests
{
    public static TheoryData<string> CaseNames => [.. DialogCaseTable.All.Select(c => c.Name)];

    public static TheoryData<string> EnterCaseNames => [.. DialogCaseTable.All.Where(c => c.EnterAccepts is not null).Select(c => c.Name)];

    [Fact]
    public void DialogCaseTable_EveryDialogType_HasACase()
    {
        var covered = DialogCaseTable.All.Select(c => c.Type).ToHashSet();
        Assert.Empty(DialogCaseTable.FindDialogTypes().Where(t => !covered.Contains(t)).Select(t => t.Name));
    }

    [AvaloniaTheory]
    [MemberData(nameof(CaseNames))]
    public void ShowAsync_EachDialog_ItsButtonsInOrderAndRedOnlyWhenRisky(string name)
    {
        var dialog = DialogCaseTable.Find(name);
        var (window, overlay) = OverlayRig.Open();
        try
        {
            _ = dialog.Open(overlay);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(dialog.Buttons(), OverlayRig.FindButtons(overlay).Select(OverlayRig.ReadText));
            Assert.Equal(dialog.Red, OverlayRig.FindMainButton(overlay).Classes.Contains("danger"));
            overlay.Close();
            Dispatcher.UIThread.RunJobs();
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Enter takes the default answer, except for deleting a user's data, restoring a backup or trusting a changed certificate.</summary>
    [AvaloniaTheory]
    [MemberData(nameof(EnterCaseNames))]
    public async Task ShowAsync_EnterPressed_AcceptsOnlyWhereThatIsSafe(string name)
    {
        var dialog = DialogCaseTable.Find(name);
        var (window, overlay) = OverlayRig.Open();
        try
        {
            var shown = dialog.Open(overlay);
            Dispatcher.UIThread.RunJobs();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(shown.IsCompleted);
            Assert.Equal(dialog.EnterAccepts, await shown);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Package 98: the dialogs that send hand their answer to the send and wait for its mark.</summary>
    [AvaloniaTheory]
    [InlineData("Ban")]
    [InlineData("Text")]
    [InlineData("Channel")]
    public async Task ShowAsync_SendingDialog_SendsTheAnswerAndClosesWhenDone(string name)
    {
        var (window, overlay) = OverlayRig.Open();
        try
        {
            var sending = new PendingActionViewModel(new ManualTimeProvider());
            var sent = new List<object>();
            PendingActionViewModel Send(object answer)
            {
                sent.Add(answer);
                sending.Start();
                return sending;
            }
            Task shown = name switch
            {
                "Ban" => BanDialog.ShowAsync(overlay, "anna", submit: Send),
                "Text" => TextDialog.ShowAsync(overlay, "Titel", "Text:", Send),
                "Channel" => ChannelDialogView.ShowAsync(overlay, new("Raid", ""), ChannelDialogMode.Edit, submit: Send),
                _ => throw new ArgumentException(name),
            };
            Dispatcher.UIThread.RunJobs();
            OverlayRig.Click(OverlayRig.FindMainButton(overlay));
            Assert.Single(sent);
            Assert.True(overlay.IsOpen); // waits for the server
            sending.Done();
            Dispatcher.UIThread.RunJobs();
            Assert.True(shown.IsCompleted);
            Assert.False(overlay.IsOpen);
        }
        finally
        {
            window.Close();
        }
    }
}
