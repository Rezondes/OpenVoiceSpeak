namespace OVS.Manager.Control;

/// <summary>Package M15: a backup the manager made for a reason (schedule, update); kept by server and file name.</summary>
internal sealed class BackupMark
{
    /// <summary>The server's id.</summary>
    public string ServerId { get; set; } = "";

    /// <summary>The backup's file name on the server.</summary>
    public string FileName { get; set; } = "";

    /// <summary>Why it was made.</summary>
    public BackupKind Kind { get; set; }

    /// <summary>The text shown with it, such as "vor Update 191026.ab".</summary>
    public string? Note { get; set; }
}
