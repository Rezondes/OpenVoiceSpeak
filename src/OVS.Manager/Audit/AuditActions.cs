namespace OVS.Manager.Audit;

/// <summary>Package M5: the actions the audit log names; later packages add theirs (servers, M7 ff.).</summary>
internal static class AuditActions
{
    /// <summary>A successful login.</summary>
    public const string Login = "Login";
    /// <summary>A failed login; the target is the name typed.</summary>
    public const string LoginFailed = "LoginFailed";
    /// <summary>A login refused for too many failures from the address.</summary>
    public const string LoginRefused = "LoginRefused";
    /// <summary>A logout.</summary>
    public const string Logout = "Logout";
    /// <summary>The first admin was made.</summary>
    public const string AdminCreated = "AdminCreated";
    /// <summary>A server manager was made.</summary>
    public const string AccountCreated = "AccountCreated";
    /// <summary>An account was locked.</summary>
    public const string AccountLocked = "AccountLocked";
    /// <summary>An account was unlocked.</summary>
    public const string AccountUnlocked = "AccountUnlocked";
    /// <summary>An account got another name.</summary>
    public const string AccountRenamed = "AccountRenamed";
    /// <summary>The admin set a new start password.</summary>
    public const string PasswordReset = "PasswordReset";
    /// <summary>An account was deleted.</summary>
    public const string AccountDeleted = "AccountDeleted";
    /// <summary>A user changed their own password.</summary>
    public const string PasswordChanged = "PasswordChanged";
    /// <summary>A user changed their own e-mail address.</summary>
    public const string EmailChanged = "EmailChanged";
    /// <summary>A user changed their own language.</summary>
    public const string LanguageChanged = "LanguageChanged";
    /// <summary>A user ended their other sessions.</summary>
    public const string SessionsEnded = "SessionsEnded";
    /// <summary>A server was created (M7).</summary>
    public const string ServerCreated = "ServerCreated";
    /// <summary>A server was started (M8).</summary>
    public const string ServerStarted = "ServerStarted";
    /// <summary>A server was stopped (M8).</summary>
    public const string ServerStopped = "ServerStopped";
    /// <summary>A server was restarted (M8).</summary>
    public const string ServerRestarted = "ServerRestarted";
    /// <summary>A server was moved to the trash (M9).</summary>
    public const string ServerDeleted = "ServerDeleted";
    /// <summary>A server came back from the trash (M9).</summary>
    public const string ServerRestored = "ServerRestored";
    /// <summary>A server's container and volume were removed for good (M9).</summary>
    public const string ServerPurged = "ServerPurged";
    /// <summary>A server was assigned to a manager (M10); the target is the manager.</summary>
    public const string ServerAssigned = "ServerAssigned";
    /// <summary>A server's assignment to a manager ended (M10); the target is the manager.</summary>
    public const string ServerUnassigned = "ServerUnassigned";
    /// <summary>The admin accepted a server's new certificate (M11); the detail is its fingerprint.</summary>
    public const string CertificateAccepted = "CertificateAccepted";
    /// <summary>A server's settings were changed (M12); the detail names the changed fields, never a password.</summary>
    public const string ServerSettingsChanged = "ServerSettingsChanged";
    /// <summary>A server's icon was set or removed (M12).</summary>
    public const string ServerIconChanged = "ServerIconChanged";
    /// <summary>A user was kicked from a server (M13); the detail is the reason.</summary>
    public const string UserKicked = "UserKicked";
    /// <summary>A user was banned on a server (M13); the detail is the reason and the duration.</summary>
    public const string UserBanned = "UserBanned";
    /// <summary>A ban on a server was lifted (M13).</summary>
    public const string UserUnbanned = "UserUnbanned";
    /// <summary>A user was server-muted (M13); the detail is the duration.</summary>
    public const string UserMuted = "UserMuted";
    /// <summary>A user's server mute was lifted (M13).</summary>
    public const string UserUnmuted = "UserUnmuted";
    /// <summary>A user was moved into another channel (M13); the detail is the channel.</summary>
    public const string UserMoved = "UserMoved";
    /// <summary>A known user was deleted with its data (M13).</summary>
    public const string KnownUserDeleted = "KnownUserDeleted";
    /// <summary>A user got a group (M13); the detail is the group.</summary>
    public const string GroupAssigned = "GroupAssigned";
    /// <summary>A user's group was taken (M13); the detail is the group.</summary>
    public const string GroupUnassigned = "GroupUnassigned";
    /// <summary>A group was created (M13).</summary>
    public const string GroupCreated = "GroupCreated";
    /// <summary>A group's name or rights changed (M13).</summary>
    public const string GroupChanged = "GroupChanged";
    /// <summary>A group was deleted (M13).</summary>
    public const string GroupDeleted = "GroupDeleted";
    /// <summary>The order of the groups changed (M13); the target is the moved group.</summary>
    public const string GroupsReordered = "GroupsReordered";
}
