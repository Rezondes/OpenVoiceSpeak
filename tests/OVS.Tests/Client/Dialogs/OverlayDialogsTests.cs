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
    /// <summary>
    /// The callbacks without a case here, each with its reason: <c>Server.EditChannel</c> opens <c>ChannelDialogView</c>, a
    /// view with its own view model and no case in <see cref="DialogCaseTable"/> (ChannelDialogViewModelTests and
    /// MotionCoverageTests open it).
    /// </summary>
    static readonly string[] NoOpenCase = ["Server.EditChannel"];

    /// <summary>Each callback path and the <see cref="DialogCaseTable"/> case of the dialog it opens.</summary>
    public static TheoryData<string, string> OpenCases => new()
    {
        { "Confirm", "Confirm" },
        { "AskPassword", "ServerPassword" },
        { "OfferUpdate", "Update" },
        { "AskLeave", "Leave" },
        { "ConnectionDetails", "ConnectionDetails" },
        { "EditBookmark", "Bookmark" },
        { "EditKeyBinding", "KeyBinding" },
        { "Server.Confirm", "Confirm" },
        { "Server.Ban", "Ban" },
        { "Server.AskText", "Text" },
        { "Server.PickChannel", "ChannelPicker" },
        { "Server.AskChannelPassword", "ChannelPassword" },
        { "Admin.AskLeave", "Leave" },
        { "Admin.UsersAndBans.Ban", "Ban" },
        { "Admin.UsersAndBans.ConfirmDeleteUser", "DeleteUser" },
        { "Admin.BackupCard.Confirm", "Confirm" },
        { "Admin.BackupCard.ConfirmRestore", "Restore" },
        { "Admin.BackupCard.ConfirmBackupDownload", "BackupDownload" },
    };

    /// <summary>Every property of the set and of each nested record, by its dotted path, with its value.</summary>
    static IEnumerable<CallbackEntry> Walk(object? record, string prefix = "")
    {
        foreach (var property in record?.GetType().GetProperties() ?? [])
        {
            var value = property.GetValue(record);
            yield return new CallbackEntry(prefix + property.Name, value);
            if (property.PropertyType.Namespace == typeof(DialogCallbacks).Namespace)
                foreach (var nested in Walk(value, prefix + property.Name + ".")) yield return nested;
        }
    }

    /// <summary>The dotted paths of every dialog delegate of the set.</summary>
    static IEnumerable<string> ListCallbackPaths(DialogCallbacks dialogs) =>
        Walk(dialogs).Where(entry => entry.Value is Delegate).Select(entry => entry.Path);

    [AvaloniaFact]
    public void Create_Overlay_SetsEveryCallbackOfEveryRecord()
    {
        var dialogs = OverlayDialogs.Create(new OverlayHost(), () => null);
        Assert.Empty(Walk(dialogs).Where(entry => entry.Value is null).Select(entry => entry.Path));
    }

    [AvaloniaFact]
    public void Create_EveryCallbackPath_HasAnOpenCase()
    {
        var withCase = OpenCases.Select(row => (string)row[0]).Concat(NoOpenCase).Order();
        Assert.Equal(ListCallbackPaths(OverlayDialogs.Create(new OverlayHost(), () => null)).Order(), withCase);
    }

    /// <summary>Calls the callback at <paramref name="path"/> as a view model would.</summary>
    static Task Call(DialogCallbacks dialogs, string path) => path switch
    {
        "Confirm" => dialogs.Confirm!("Wirklich?"),
        "AskPassword" => dialogs.AskPassword!("Gilde"),
        "OfferUpdate" => dialogs.OfferUpdate!(new UpdateOffer("280926.0b2c", "deploy-bbbbbbb", "- Neu", DateTimeOffset.UtcNow,
            new Uri("https://example.org/a"), new Uri("https://example.org/b"))),
        "AskLeave" => dialogs.AskLeave!(),
        "ConnectionDetails" => dialogs.ConnectionDetails!(ConnectionStats.CreateBlank()),
        "EditBookmark" => dialogs.EditBookmark!(new Bookmark("Gilde", "h", 7000, "ich")),
        "EditKeyBinding" => dialogs.EditKeyBinding!(null, _ => Task.FromResult<OVS.Client.Input.KeyChord?>(null)),
        "Server.Confirm" => dialogs.Server.Confirm!("Wirklich?"),
        "Server.Ban" => dialogs.Server.Ban!("anna", true, null),
        "Server.AskText" => dialogs.Server.AskText!("Titel", "Text:", null),
        "Server.PickChannel" => dialogs.Server.PickChannel!("Verschieben nach", [], null),
        "Server.AskChannelPassword" => dialogs.Server.AskChannelPassword!("Raid"),
        "Admin.AskLeave" => dialogs.Admin.AskLeave!(),
        "Admin.UsersAndBans.Ban" => dialogs.Admin.UsersAndBans.Ban!("anna", true, null),
        "Admin.UsersAndBans.ConfirmDeleteUser" => dialogs.Admin.UsersAndBans.ConfirmDeleteUser!("anna"),
        "Admin.BackupCard.Confirm" => dialogs.Admin.BackupCard.Confirm!("Wirklich?"),
        "Admin.BackupCard.ConfirmRestore" => dialogs.Admin.BackupCard.ConfirmRestore!("Sicherung"),
        "Admin.BackupCard.ConfirmBackupDownload" => dialogs.Admin.BackupCard.ConfirmBackupDownload!(),
        _ => throw new ArgumentException(path),
    };

    [AvaloniaTheory]
    [MemberData(nameof(OpenCases))]
    public void Create_EachCallback_OpensItsDialog(string path, string dialog)
    {
        var expected = DialogCaseTable.Find(dialog);
        var (window, overlay) = OverlayRig.Open();
        try
        {
            _ = Call(OverlayDialogs.Create(overlay, () => null), path);
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

    /// <summary>One property of the set or a nested record: its dotted path and its value.</summary>
    sealed record CallbackEntry(string Path, object? Value);
}
