using OVS.Client.Controls;
using OVS.Client.Dialogs;
using OVS.Client.Input;
using OVS.Client.Localization;
using OVS.Client.Net;
using OVS.Client.Net.Updates;
using OVS.Client.Settings;
using OVS.Client.ViewModels;

namespace OVS.Tests.TestSupport;

/// <summary>Every dialog type of <c>OVS.Client.Dialogs</c> and at least one case of each.</summary>
static class DialogCaseTable
{
    /// <summary>The types of the dialogs namespace whose name ends in "Dialog" (the frame and its helpers do not).</summary>
    public static List<Type> FindDialogTypes() =>
        typeof(DialogFrame).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(DialogFrame).Namespace && t.Name.EndsWith("Dialog", StringComparison.Ordinal))
            .OrderBy(t => t.Name, StringComparer.Ordinal).ToList();

    public static readonly IReadOnlyList<DialogCase> All =
    [
        new("Confirm", typeof(ConfirmDialog), o => ConfirmDialog.ShowAsync(o, "Wirklich?"),
            () => [Strings.Dlg_Cancel, Strings.Dlg_YesDelete], Red: true, EnterAccepts: true),
        new("DeleteUser", typeof(DeleteUserDialog), o => DeleteUserDialog.ShowAsync(o, "anna"),
            () => [Strings.Dlg_Cancel, Strings.Dlg_DeleteForever], Red: true, EnterAccepts: false),
        new("Restore", typeof(RestoreDialog), o => RestoreDialog.ShowAsync(o, "Sicherung"),
            () => [Strings.Dlg_Cancel, Strings.Ui_Restore], Red: true, EnterAccepts: false),
        new("BackupDownload", typeof(BackupDownloadDialog), BackupDownloadDialog.ShowAsync,
            () => [Strings.Dlg_Cancel, Strings.Ui_Download], Red: false, EnterAccepts: true),
        new("TofuUnknown", typeof(TofuDialog), o => TofuDialog.ShowAsync(o, new TofuPrompt("h", 1, new string('a', 64), TofuResult.Unknown)),
            () => [Strings.Dlg_Cancel, Strings.Tofu_Trust], Red: false, EnterAccepts: true),
        new("TofuMismatch", typeof(TofuDialog), o => TofuDialog.ShowAsync(o, new TofuPrompt("h", 1, new string('a', 64), TofuResult.Mismatch)),
            () => [Strings.Dlg_Cancel, Strings.Tofu_TrustAnyway], Red: true, EnterAccepts: false),
        new("Update", typeof(UpdateOfferDialog), o => UpdateOfferDialog.ShowAsync(o, new UpdateOffer("280926.0b2c", "deploy-bbbbbbb", "- Neu",
                DateTimeOffset.UtcNow, new Uri("https://example.org/a"), new Uri("https://example.org/b"))),
            () => [Strings.Dlg_Later, Strings.Dlg_InstallNow], Red: false, EnterAccepts: true),
        new("Leave", typeof(LeaveDialog), o => Accepted(LeaveDialog.ShowAsync(o), choice => choice == LeaveChoice.Save),
            () => [Strings.Dlg_SaveAndLeave, Strings.Dlg_LeaveWithoutSaving, Strings.Dlg_KeepEditing], Red: false, EnterAccepts: true),
        new("ConnectionDetails", typeof(ConnectionDetailsDialog), o => Accepted(ConnectionDetailsDialog.ShowAsync(o, new ConnectionStatsViewModel())),
            () => [Strings.Ui_Close], Red: false, EnterAccepts: true),
        new("Ban", typeof(BanDialog), o => Accepted(BanDialog.ShowAsync(o, "anna"), choice => choice is not null),
            () => [Strings.Dlg_Cancel, Strings.Dlg_Ban], Red: true, EnterAccepts: null),
        new("Text", typeof(TextDialog), o => Accepted(TextDialog.ShowAsync(o, "Titel", "Text:"), text => text is not null),
            () => [Strings.Dlg_Cancel, "OK"], Red: false, EnterAccepts: null),
        new("ServerPassword", typeof(ServerPasswordDialog), o => Accepted(ServerPasswordDialog.ShowAsync(o, "Gilde"), answer => answer is not null),
            () => [Strings.Dlg_Cancel, Strings.Dlg_Connect], Red: false, EnterAccepts: null),
        new("ChannelPassword", typeof(ChannelPasswordDialog), o => Accepted(ChannelPasswordDialog.ShowAsync(o, "Raid"), answer => answer is not null),
            () => [Strings.Dlg_Cancel, Strings.Ui_Join], Red: false, EnterAccepts: null),
        new("Bookmark", typeof(BookmarkDialog), o => Accepted(BookmarkDialog.ShowAsync(o, new Bookmark("Gilde", "h", 7000, "ich")), edit => edit is not null),
            () => [Strings.Dlg_Cancel, Strings.Dlg_Save], Red: false, EnterAccepts: null),
        new("KeyBinding", typeof(KeyBindingDialog), o => Accepted(KeyBindingDialog.ShowAsync(o, null, _ => Task.FromResult<KeyChord?>(null)), binding => binding is not null),
            () => [Strings.Dlg_Cancel, Strings.Dlg_Save], Red: false, EnterAccepts: null),
        new("Connect", typeof(ConnectDialog), o => Accepted(ConnectDialog.ShowAsync(o, []), choice => choice is not null),
            () => [Strings.Dlg_Cancel, Strings.Dlg_Connect], Red: false, EnterAccepts: null),
        // no channel: nothing to select, so the plain "OK"
        new("ChannelPicker", typeof(ChannelPickerDialog), o => Accepted(ChannelPickerDialog.ShowAsync(o, "Verschieben nach", []), channel => channel is not null),
            () => [Strings.Dlg_Cancel, "OK"], Red: false, EnterAccepts: null),
    ];

    public static DialogCase Find(string name) => All.Single(c => c.Name == name);

    static async Task<bool> Accepted<T>(Task<T> shown, Func<T, bool> accepted) => accepted(await shown);

    static async Task<bool> Accepted(Task shown)
    {
        await shown;
        return true;
    }
}
