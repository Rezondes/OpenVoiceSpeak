using OVS.Client.Collections;
using OVS.Client.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OVS.Shared.Protocol.Messages.ServerBackups;

namespace OVS.Client.ViewModels;

/// <summary>Package 74: one backup on the server: date as title, size and server version below.</summary>
/// <param name="canRestore">Package 89: only members of the Admin group see the restore button.</param>
/// <param name="info">The backup.</param>
/// <param name="delete">Deletes it after asking.</param>
/// <param name="restore">Restores it after asking.</param>
public sealed partial class BackupViewModel(BackupInfo info, Func<BackupViewModel, Task> delete, Func<BackupViewModel, Task> restore, bool canRestore = false)
    : ObservableObject, IMotionKey
{
    /// <summary>The file name: the same backup keeps its motion when the list is rebuilt.</summary>
    public object MotionKey => Info.FileName;
    /// <summary>The backup as the server lists it.</summary>
    public BackupInfo Info { get; } = info;
    /// <summary>The restore button shows.</summary>
    public bool CanRestore { get; } = canRestore;
    /// <summary>The creation time as the card's title.</summary>
    public string Title => TitleOf(Info);
    /// <summary>A backup's creation time in local time, as its title.</summary>
    public static string TitleOf(BackupInfo info) => info.CreatedAt.ToLocalTime().ToString("G") +
                                                     (info.FileName.StartsWith(SafetyPrefix, StringComparison.Ordinal) ? " " + Strings.Backup_Safety : "");
    /// <summary>Size and server version.</summary>
    public string Details => string.Format(Strings.Backup_Details, Formats.Size(Info.Size), Info.ServerVersion);

    /// <summary>The prefix the server gives the backup it takes before a restore.</summary>
    const string SafetyPrefix = "vor-wiederherstellung_";

    [RelayCommand]
    Task DeleteAsync() => delete(this);

    [RelayCommand]
    Task RestoreAsync() => restore(this);
}
