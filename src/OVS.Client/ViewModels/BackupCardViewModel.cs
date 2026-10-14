using System.Globalization;
using OVS.Client.Collections;
using OVS.Client.Dialogs;
using OVS.Client.Localization;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OVS.Client.Services;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.ServerBackups;

namespace OVS.Client.ViewModels;

/// <summary>
/// The backup card of the server tab: the backups on the server, creating, deleting and restoring them, and their
/// download to and upload from this PC (through the transfer the logs tab shares).
/// </summary>
internal sealed partial class BackupCardViewModel : ObservableObject
{
    readonly ServerViewModel server;
    readonly BackupCardDialogs dialogs;
    // Package 87: the backup list comes in pages, collected until complete
    readonly PagedListCollector<BackupInfo> backupPages = new();
    IReadOnlyList<BackupInfo> backupInfos = [];
    bool backupsForAdmin;

    internal BackupCardViewModel(ServerViewModel server, AdminTransfer transfer, BackupCardDialogs? dialogs = null)
    {
        this.server = server;
        this.dialogs = dialogs ?? new BackupCardDialogs();
        Transfer = transfer;
        BackupsLoad = server.NewPending();
        BackupCreate = server.NewPending();
        Restoring = server.NewPending();
    }

    /// <summary>
    /// Package 89: the backups are shown and managed with BackupsManage. The page raises the change notification for the
    /// bound card (<c>AdminViewModel.ShowBackups</c>).
    /// </summary>
    internal bool ShowBackups => server.SelfPermissions.Has(Permission.BackupsManage);

    /// <summary>Package 89 (A101): members of the Admin group only, as on the server; all rights alone are not enough.</summary>
    internal bool CanUploadRestore => server.IsAdmin;

    /// <summary>The page's one transfer, see <see cref="AdminViewModel.Transfer"/>.</summary>
    internal AdminTransfer Transfer { get; }

    /// <summary>Package 97 (A113): the backups' loading state, and the backup actions that wait for the server.</summary>
    internal PendingActionViewModel BackupsLoad { get; }
    internal PendingActionViewModel BackupCreate { get; }
    internal PendingActionViewModel Restoring { get; }

    /// <summary>Package 74: the backups on the server, newest first.</summary>
    internal ObservableCollection<BackupViewModel> Backups { get; } = [];
    internal bool HasNoBackups => !Backups.Live().Any();

    /// <summary>Asks for the backup list when the backups are shown (Package 154: also the page's refresh button).</summary>
    [RelayCommand]
    internal Task RefreshAsync() => ShowBackups ? SendRoutedAsync(new ListBackups(), pending: BackupsLoad) : Task.CompletedTask;

    /// <summary>The page closed: no answer reaches this section.</summary>
    internal void Detach()
    {
        // ponytail: every request keeps its route until here; same ceiling as UsersAndBansViewModel.Detach
        server.Routes.RemoveAll(Apply);
    }

    /// <summary>The server's state changed: the restore buttons come and go with the Admin group (Package 89).</summary>
    internal void FollowServer()
    {
        if (CanUploadRestore != backupsForAdmin) RebuildBackups();
    }

    /// <summary>Sends a request whose answers, and the server's error for it, come back to <see cref="Apply"/>.</summary>
    Task<string> SendRoutedAsync(Request request, string? id = null, PendingActionViewModel? pending = null) => server.SendAsync(request, id, pending, Apply);

    /// <summary>The server's answers to this section's own requests (sent with this as their route) and its errors for them.</summary>
    internal void Apply(Message answer)
    {
        if (answer is not BackupList list) return;
        if (backupPages.Add(list.RequestId, list.Offset, list.Total, list.Backups, (roundId, offset) => _ = SendRoutedAsync(new ListBackups(offset), roundId)) is not { } backups) return;
        backupInfos = backups;
        BackupsLoad.Done();
        BackupCreate.Done(); // the new backup is in the list (Restoring waits for the disconnect)
        RebuildBackups();
    }

    void RebuildBackups()
    {
        backupsForAdmin = CanUploadRestore;
        OnPropertyChanged(nameof(CanUploadRestore));
        CollectionSync.Sync(Backups, backupInfos.Select(info => new BackupViewModel(info, DeleteBackupAsync, RestoreBackupAsync, backupsForAdmin)).ToList(), server.Leave);
        OnPropertyChanged(nameof(HasNoBackups));
    }

    /// <summary>Package 89 (A101): asked before the file picker; the archive holds the certificate with its private key and all user data.</summary>
    internal async Task<bool> ConfirmDownloadAsync() => dialogs.ConfirmBackupDownload is { } warn && await warn();

    /// <summary>Answered with the new list; Package 97: busy until then, a second click meanwhile is ignored.</summary>
    [RelayCommand]
    Task NewBackupAsync() => BackupCreate.IsRunning ? Task.CompletedTask : SendRoutedAsync(new CreateBackup(), pending: BackupCreate);

    async Task DeleteBackupAsync(BackupViewModel backup)
    {
        if (dialogs.Confirm is not { } confirm || !await confirm(TextFormat.Of(Strings.Backup_ConfirmDelete, backup.Title))) return;
        await SendRoutedAsync(new DeleteBackup(backup.Info.FileName)); // answered with the new list
    }

    /// <summary>Everyone, this client too, is disconnected with Restoring once the server accepted the backup.</summary>
    async Task RestoreBackupAsync(BackupViewModel backup)
    {
        if (!CanUploadRestore) return;
        await ConfirmAndRestoreAsync(backup.Info);
    }

    /// <summary>Asks the red question about the backup; restores it only when answered yes. Package 97: busy until the disconnect.</summary>
    async Task ConfirmAndRestoreAsync(BackupInfo backup)
    {
        if (dialogs.ConfirmRestore is not { } confirm || !await confirm(BackupViewModel.TitleOf(backup))) return;
        await server.SendAsync(new RestoreBackup(backup.FileName), pending: Restoring);
    }

    // ---- Package 75: download to and upload from this PC, chunk by chunk (A91) ----

    /// <summary>Saves the backup to target (chosen in the save dialog).</summary>
    internal Task DownloadBackupAsync(BackupViewModel backup, string target) => Transfer.DownloadBackupAsync(backup.Info.FileName, target);

    /// <summary>Uploads the file; with restore, asks the red question of Package 74 once it is stored.</summary>
    internal async Task UploadBackupAsync(string path, bool restore)
    {
        if (!CanUploadRestore || await Transfer.UploadBackupAsync(path, Apply) is not { } uploaded || !restore) return;
        await ConfirmAndRestoreAsync(uploaded);
    }
}
