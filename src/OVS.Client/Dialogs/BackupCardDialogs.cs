namespace OVS.Client.Dialogs;

/// <summary>The dialogs of the backup card. Unset entries mean "cancelled" (used by tests).</summary>
internal sealed record BackupCardDialogs
{
    /// <summary>Package 89 (A101): the warning before a download, the file holds the server's private key and user data.</summary>
    public Func<Task<bool>>? ConfirmBackupDownload { get; init; }
    /// <summary>Asks a yes or no question.</summary>
    public Func<string, Task<bool>>? Confirm { get; init; }
    /// <summary>Package 74: the red question before a backup replaces the server's state (argument: the backup's title).</summary>
    public Func<string, Task<bool>>? ConfirmRestore { get; init; }
}
