namespace OVS.Client.Dialogs;

/// <summary>The dialogs the administration page asks, and those of its sections. Unset entries mean "cancelled" (used by tests).</summary>
public sealed record AdminDialogs
{
    /// <summary>Package 114: unsaved changes would be lost; unset means leave without saving (as before).</summary>
    public Func<Task<LeaveChoice>>? AskLeave { get; init; }

    /// <summary>What the users and bans tabs ask (handed over by the page).</summary>
    public UsersAndBansDialogs UsersAndBans { get; init; } = new();

    /// <summary>What the backup card asks (handed over by the page).</summary>
    public BackupCardDialogs BackupCard { get; init; } = new();
}
