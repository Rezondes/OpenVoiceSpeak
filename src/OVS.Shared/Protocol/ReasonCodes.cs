namespace OVS.Shared.Protocol;

/// <summary>Every reason code an Error, Rejected or Disconnected carries; ConnectionLost is set by the client itself.</summary>
public static class ReasonCodes
{
    // Rejected (handshake)
    /// <summary>Rejected: the client speaks another protocol version.</summary>
    public const string VersionMismatch = nameof(VersionMismatch);
    /// <summary>Rejected: the client's proof does not match its public key.</summary>
    public const string BadSignature = nameof(BadSignature);
    /// <summary>Rejected: the server password is wrong.</summary>
    public const string WrongPassword = nameof(WrongPassword);
    /// <summary>Package 191: a service session was asked for by someone outside Server-Admin.</summary>
    public const string ServiceNotAllowed = nameof(ServiceNotAllowed);
    /// <summary>Rejected: the server's user limit is reached.</summary>
    public const string ServerFull = nameof(ServerFull);
    /// <summary>Rejected: the nickname breaks the name rules.</summary>
    public const string NicknameInvalid = nameof(NicknameInvalid);
    /// <summary>Rejected: someone else online has the nickname.</summary>
    public const string NicknameTaken = nameof(NicknameTaken);
    /// <summary>Rejected: too many connections from the address.</summary>
    public const string TooManyConnections = nameof(TooManyConnections);
    /// <summary>Rejected: too many wrong passwords from the address; it waits.</summary>
    public const string TooManyPasswordAttempts = nameof(TooManyPasswordAttempts);
    /// <summary>Rejected: the handshake took too long.</summary>
    public const string Timeout = nameof(Timeout);
    /// <summary>Rejected: the user is banned.</summary>
    public const string Banned = nameof(Banned);
    /// <summary>Rejected: the handshake broke the protocol.</summary>
    public const string ProtocolError = nameof(ProtocolError);

    // Disconnected
    /// <summary>Disconnected: the same identity connected again.</summary>
    public const string ReplacedByNewConnection = nameof(ReplacedByNewConnection);
    /// <summary>Disconnected: the server stops.</summary>
    public const string ServerShutdown = nameof(ServerShutdown);
    /// <summary>Disconnected: the server restarts.</summary>
    public const string ServerRestart = nameof(ServerRestart);
    /// <summary>Disconnected: a moderator kicked the user.</summary>
    public const string Kicked = nameof(Kicked);
    /// <summary>Disconnected: the user's stored data was deleted (Package 72).</summary>
    public const string UserDeleted = nameof(UserDeleted);
    /// <summary>Disconnected: the server restarts on a backup (Package 74).</summary>
    public const string Restoring = nameof(Restoring);
    /// <summary>The connection broke; set by the client only.</summary>
    public const string ConnectionLost = nameof(ConnectionLost);

    // Error (requests)
    /// <summary>Error: the request needs a right the user lacks.</summary>
    public const string PermissionDenied = nameof(PermissionDenied);
    /// <summary>Error: the named channel, user, group or file is unknown.</summary>
    public const string NotFound = nameof(NotFound);
    /// <summary>Error: the name breaks the name rules.</summary>
    public const string InvalidName = nameof(InvalidName);
    /// <summary>Error: another channel or group has the name.</summary>
    public const string NameTaken = nameof(NameTaken);
    /// <summary>Error: a value is out of its range or malformed.</summary>
    public const string InvalidValue = nameof(InvalidValue);
    /// <summary>Error: the default channel cannot be deleted.</summary>
    public const string CannotDeleteDefault = nameof(CannotDeleteDefault);
    /// <summary>Error: the admin token is wrong or used.</summary>
    public const string InvalidToken = nameof(InvalidToken);
    /// <summary>Error: Guest and Admin cannot be changed that way.</summary>
    public const string ProtectedGroup = nameof(ProtectedGroup);
    /// <summary>Error: the change would leave the server without an admin.</summary>
    public const string LastAdmin = nameof(LastAdmin);
    /// <summary>Error: the link is to the channel itself, to a separator or exists already.</summary>
    public const string InvalidLink = nameof(InvalidLink);
    /// <summary>Error: the server does not know the request.</summary>
    public const string UnknownRequest = nameof(UnknownRequest);
    /// <summary>Error: too many requests of this kind; try again later.</summary>
    public const string RateLimited = nameof(RateLimited);
    /// <summary>Error: the channel is full.</summary>
    public const string ChannelFull = nameof(ChannelFull);
    /// <summary>Error: a separator cannot be joined (Package 111).</summary>
    public const string NotJoinable = nameof(NotJoinable);
    /// <summary>Error: the user is in none of the channel's groups (Package 93).</summary>
    public const string ChannelLocked = nameof(ChannelLocked);
    /// <summary>Error: the channel needs its password, also for moving someone into it (Package 94).</summary>
    public const string ChannelPasswordRequired = nameof(ChannelPasswordRequired);
    /// <summary>Error: the channel password is wrong (Package 94).</summary>
    public const string WrongChannelPassword = nameof(WrongChannelPassword);
    /// <summary>Error: the archive is no valid backup (Package 74).</summary>
    public const string InvalidBackup = nameof(InvalidBackup);
    /// <summary>Error: the upload is over MaxBackupUploadBytes (Package 75).</summary>
    public const string BackupTooLarge = nameof(BackupTooLarge);
    /// <summary>Error: the log download is over ServerTuning.MaxLogDownloadBytes (Package 82).</summary>
    public const string LogsTooLarge = nameof(LogsTooLarge);
    /// <summary>Error: too many backups or too many bytes in total (Package 89).</summary>
    public const string BackupQuotaExceeded = nameof(BackupQuotaExceeded);

    /// <summary>Every code above.</summary>
    public static IEnumerable<string> All() =>
        typeof(ReasonCodes).GetFields().Where(fieldInfo => fieldInfo.IsLiteral).Select(fieldInfo => (string)fieldInfo.GetRawConstantValue()!);
}
