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
            .Where(candidate => candidate.Namespace == typeof(DialogFrame).Namespace && candidate.Name.EndsWith("Dialog", StringComparison.Ordinal))
            .OrderBy(candidate => candidate.Name, StringComparer.Ordinal).ToList();

    public static readonly IReadOnlyList<DialogCase> All =
    [
        new("Confirm", typeof(ConfirmDialog), overlayHost => ConfirmDialog.ShowAsync(overlayHost, "Wirklich?"),
            () => [Strings.Dlg_Cancel, Strings.Dlg_YesDelete], Red: true, EnterAccepts: true),
        new("DeleteUser", typeof(DeleteUserDialog), overlayHost => DeleteUserDialog.ShowAsync(overlayHost, "anna"),
            () => [Strings.Dlg_Cancel, Strings.Dlg_DeleteForever], Red: true, EnterAccepts: false),
        new("Restore", typeof(RestoreDialog), overlayHost => RestoreDialog.ShowAsync(overlayHost, "Sicherung"),
            () => [Strings.Dlg_Cancel, Strings.Ui_Restore], Red: true, EnterAccepts: false),
        new("BackupDownload", typeof(BackupDownloadDialog), BackupDownloadDialog.ShowAsync,
            () => [Strings.Dlg_Cancel, Strings.Ui_Download], Red: false, EnterAccepts: true),
        new("TofuUnknown", typeof(TofuDialog), overlayHost => TofuDialog.ShowAsync(overlayHost, new TofuPrompt("h", 1, new string('a', 64), TofuResult.Unknown)),
            () => [Strings.Dlg_Cancel, Strings.Tofu_Trust], Red: false, EnterAccepts: true),
        new("TofuMismatch", typeof(TofuDialog), overlayHost => TofuDialog.ShowAsync(overlayHost, new TofuPrompt("h", 1, new string('a', 64), TofuResult.Mismatch)),
            () => [Strings.Dlg_Cancel, Strings.Tofu_TrustAnyway], Red: true, EnterAccepts: false),
        new("Update", typeof(UpdateOfferDialog), overlayHost => UpdateOfferDialog.ShowAsync(overlayHost, new UpdateOffer("280926.0b2c", "deploy-bbbbbbb", "- Neu",
                DateTimeOffset.UtcNow, new Uri("https://example.org/a"), new Uri("https://example.org/b"))),
            () => [Strings.Dlg_Later, Strings.Dlg_InstallNow], Red: false, EnterAccepts: true),
        new("Leave", typeof(LeaveDialog), overlayHost => Accepted(LeaveDialog.ShowAsync(overlayHost), choice => choice == LeaveChoice.Save),
            () => [Strings.Dlg_SaveAndLeave, Strings.Dlg_LeaveWithoutSaving, Strings.Dlg_KeepEditing], Red: false, EnterAccepts: true),
        new("ConnectionDetails", typeof(ConnectionDetailsDialog), overlayHost => Accepted(ConnectionDetailsDialog.ShowAsync(overlayHost, ConnectionStats.CreateBlank())),
            () => [Strings.Ui_Close], Red: false, EnterAccepts: true),
        new("Ban", typeof(BanDialog), overlayHost => Accepted(BanDialog.ShowAsync(overlayHost, "anna"), choice => choice is not null),
            () => [Strings.Dlg_Cancel, Strings.Dlg_Ban], Red: true, EnterAccepts: null),
        new("Text", typeof(TextDialog), overlayHost => Accepted(TextDialog.ShowAsync(overlayHost, "Titel", "Text:"), text => text is not null),
            () => [Strings.Dlg_Cancel, "OK"], Red: false, EnterAccepts: null),
        new("ServerPassword", typeof(ServerPasswordDialog), overlayHost => Accepted(ServerPasswordDialog.ShowAsync(overlayHost, "Gilde"), answer => answer is not null),
            () => [Strings.Dlg_Cancel, Strings.Dlg_Connect], Red: false, EnterAccepts: null),
        new("ChannelPassword", typeof(ChannelPasswordDialog), overlayHost => Accepted(ChannelPasswordDialog.ShowAsync(overlayHost, "Raid"), answer => answer is not null),
            () => [Strings.Dlg_Cancel, Strings.Ui_Join], Red: false, EnterAccepts: null),
        new("Bookmark", typeof(BookmarkDialog), overlayHost => Accepted(BookmarkDialog.ShowAsync(overlayHost, new Bookmark("Gilde", "h", 7000, "ich")), edit => edit is not null),
            () => [Strings.Dlg_Cancel, Strings.Dlg_Save], Red: false, EnterAccepts: null),
        new("KeyBinding", typeof(KeyBindingDialog), overlayHost => Accepted(KeyBindingDialog.ShowAsync(overlayHost, null, _ => Task.FromResult<KeyChord?>(null)), binding => binding is not null),
            () => [Strings.Dlg_Cancel, Strings.Dlg_Save], Red: false, EnterAccepts: null),
        new("Connect", typeof(ConnectDialog), overlayHost => Accepted(ConnectDialog.ShowAsync(overlayHost, []), choice => choice is not null),
            () => [Strings.Dlg_Cancel, Strings.Dlg_Connect], Red: false, EnterAccepts: null),
        // no channel: nothing to select, so the plain "OK"
        new("ChannelPicker", typeof(ChannelPickerDialog), overlayHost => Accepted(ChannelPickerDialog.ShowAsync(overlayHost, "Verschieben nach", []), channel => channel is not null),
            () => [Strings.Dlg_Cancel, "OK"], Red: false, EnterAccepts: null),
    ];

    public static DialogCase Find(string name) => All.Single(dialogCase => dialogCase.Name == name);

    static async Task<bool> Accepted<T>(Task<T> shown, Func<T, bool> accepted) => accepted(await shown);

    static async Task<bool> Accepted(Task shown)
    {
        await shown;
        return true;
    }
}
