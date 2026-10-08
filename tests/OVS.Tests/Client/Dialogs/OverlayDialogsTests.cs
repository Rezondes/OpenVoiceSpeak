using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OVS.Client.Controls;
using OVS.Client.Dialogs;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Client.Net.Updates;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Dialogs;

/// <summary>The overlay's dialogs as the view models ask for them: every callback set, each to its own dialog.</summary>
public sealed class OverlayDialogsTests
{
    [AvaloniaFact]
    public void Create_Overlay_SetsEveryCallback()
    {
        var callbacks = OverlayDialogs.Create(new OverlayHost(), () => null);
        var unset = typeof(DialogCallbacks).GetProperties().Where(p => p.GetValue(callbacks) is null).Select(p => p.Name);
        Assert.Empty(unset);
    }

    /// <summary>Calls the callback <paramref name="callback"/> as a view model would.</summary>
    static Task Call(DialogCallbacks dialogs, string callback) => callback switch
    {
        "Confirm" => dialogs.Confirm!("Wirklich?"),
        "ConfirmDeleteUser" => dialogs.ConfirmDeleteUser!("anna"),
        "ConfirmRestore" => dialogs.ConfirmRestore!("Sicherung"),
        "ConfirmBackupDownload" => dialogs.ConfirmBackupDownload!(),
        "AskPassword" => dialogs.AskPassword!("Gilde"),
        "AskChannelPassword" => dialogs.AskChannelPassword!("Raid"),
        "OfferUpdate" => dialogs.OfferUpdate!(new UpdateOffer("280926.0b2c", "deploy-bbbbbbb", "- Neu", DateTimeOffset.UtcNow,
            new Uri("https://example.org/a"), new Uri("https://example.org/b"))),
        "AskLeave" => dialogs.AskLeave!(),
        "Ban" => dialogs.Ban!("anna", true, null),
        "ConnectionDetails" => dialogs.ConnectionDetails!(new ConnectionStatsViewModel()),
        "EditBookmark" => dialogs.EditBookmark!(new Bookmark("Gilde", "h", 7000, "ich")),
        "EditKeyBinding" => dialogs.EditKeyBinding!(null, _ => Task.FromResult<OVS.Client.Input.KeyChord?>(null)),
        "AskText" => dialogs.AskText!("Titel", "Text:", null),
        "PickChannel" => dialogs.PickChannel!("Verschieben nach", [], null),
        _ => throw new ArgumentException(callback),
    };

    [AvaloniaTheory]
    [InlineData("Confirm", "Confirm")]
    [InlineData("ConfirmDeleteUser", "DeleteUser")]
    [InlineData("ConfirmRestore", "Restore")]
    [InlineData("ConfirmBackupDownload", "BackupDownload")]
    [InlineData("AskPassword", "ServerPassword")]
    [InlineData("AskChannelPassword", "ChannelPassword")]
    [InlineData("OfferUpdate", "Update")]
    [InlineData("AskLeave", "Leave")]
    [InlineData("Ban", "Ban")]
    [InlineData("ConnectionDetails", "ConnectionDetails")]
    [InlineData("EditBookmark", "Bookmark")]
    [InlineData("EditKeyBinding", "KeyBinding")]
    [InlineData("AskText", "Text")]
    [InlineData("PickChannel", "ChannelPicker")]
    public void Create_EachCallback_OpensItsDialog(string callback, string dialog)
    {
        var expected = DialogCaseTable.Find(dialog);
        var (window, overlay) = OverlayRig.Open();
        try
        {
            _ = Call(OverlayDialogs.Create(overlay, () => null), callback);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(expected.Buttons(), OverlayRig.FindButtons(overlay).Select(OverlayRig.ReadText));
            Assert.Equal(expected.Red, OverlayRig.FindMainButton(overlay).Classes.Contains("danger"));
            overlay.Close();
            Dispatcher.UIThread.RunJobs();
        }
        finally
        {
            window.Close();
        }
    }
}
