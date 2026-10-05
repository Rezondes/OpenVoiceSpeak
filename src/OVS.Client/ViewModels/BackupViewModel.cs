using OVS.Client.Collections;
using OVS.Client.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OVS.Shared.Protocol.Messages.ServerBackups;

namespace OVS.Client.ViewModels;

/// <summary>Package 74: one backup on the server: date as title, size and server version below.</summary>
/// <param name="canRestore">Package 89: only members of the Admin group see the restore button.</param>
public sealed partial class BackupViewModel(BackupInfo info, Func<BackupViewModel, Task> delete, Func<BackupViewModel, Task> restore, bool canRestore = false)
    : ObservableObject, IMotionKey
{
    public object MotionKey => Info.FileName;
    public BackupInfo Info { get; } = info;
    public bool CanRestore { get; } = canRestore;
    public string Title => TitleOf(Info);
    public static string TitleOf(BackupInfo info) => info.CreatedAt.ToLocalTime().ToString("G") +
                                                     (info.FileName.StartsWith(SafetyPrefix, StringComparison.Ordinal) ? " " + Strings.Backup_Safety : "");
    public string Details => string.Format(Strings.Backup_Details, Formats.Size(Info.Size), Info.ServerVersion);

    /// <summary>The prefix the server gives the backup it takes before a restore.</summary>
    const string SafetyPrefix = "vor-wiederherstellung_";

    [RelayCommand]
    Task Delete() => delete(this);

    [RelayCommand]
    Task Restore() => restore(this);
}
