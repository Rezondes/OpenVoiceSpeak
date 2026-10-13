using System.Net;
using Microsoft.Extensions.Logging;
using OVS.Shared.Logging;
using OVS.Shared.Permissions;

namespace OVS.Server.Logging;

/// <summary>
/// Step 31 (N5): every line of the server log, in English, as one source-generated logger message each. The file
/// holds only the time stamp and the message (see <see cref="LogFileLoggerProvider"/>); the level is for the reader of
/// the code. A line meant only for the console or only for the file carries the event name
/// <see cref="LogFileLoggerProvider.ConsoleOnly"/> or <see cref="LogFileLoggerProvider.FileOnly"/>.
/// </summary>
static partial class ServerLogLines
{
    // Start and end of a run (ServerHost, RestartSchedule)

    [LoggerMessage(Level = LogLevel.Information, Message = "OpenVoiceSpeak server starting")]
    public static partial void Starting(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "OpenVoiceSpeak server starting (automatic restart)")]
    public static partial void StartingAfterRestart(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Version {Version}")]
    public static partial void Version(this ILogger logger, string version);

    /// <summary>The console gets the same text as an error of its own.</summary>
    [LoggerMessage(EventId = 2, EventName = "FileOnly", Level = LogLevel.Error, Message = "Start failed: {Reason}")]
    public static partial void StartFailed(this ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Listening on {Endpoint} (TCP and UDP)")]
    public static partial void Listening(this ILogger logger, IPEndPoint endpoint);

    [LoggerMessage(Level = LogLevel.Information, Message = "Certificate fingerprint: {Fingerprint}")]
    public static partial void CertificateFingerprint(this ILogger logger, string fingerprint);

    [LoggerMessage(Level = LogLevel.Information, Message = "Automatic restart daily at {At:HH:mm:ss}, the next on {Next:yyyy-MM-dd HH:mm:ss}")]
    public static partial void AutoRestartScheduled(this ILogger logger, TimeOnly at, DateTimeOffset next);

    [LoggerMessage(Level = LogLevel.Information, Message = "Automatic restart switched off")]
    public static partial void AutoRestartOff(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Restoring from a backup ...")]
    public static partial void EndingForRestore(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Automatic restart ...")]
    public static partial void EndingForRestart(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Shutting down ...")]
    public static partial void ShuttingDown(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Restore from a backup requested while shutting down ...")]
    public static partial void RestoreRequestedWhileEnding(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Backup applied (data version {DataVersion}, server version {ServerVersion})")]
    public static partial void BackupApplied(this ILogger logger, int dataVersion, string serverVersion);

    [LoggerMessage(Level = LogLevel.Error, Message = "Applying the backup failed: {Reason}")]
    public static partial void BackupApplyFailed(this ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Server stopped")]
    public static partial void Stopped(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Server stopped, restarting")]
    public static partial void StoppedForRestart(this ILogger logger);

    // State at start (ServerState)

    /// <summary>A secret: console only, never in a log file (files end up in backups).</summary>
    [LoggerMessage(EventId = 1, EventName = "ConsoleOnly", Level = LogLevel.Information,
        Message = "Admin token: {Token}  (enter it in the client under 'Redeem admin token')")]
    public static partial void AdminToken(this ILogger logger, string token);

#pragma warning disable SYSLIB1025 // the event name is the provider's routing marker, which StartFailed carries as well
    [LoggerMessage(EventId = 3, EventName = "FileOnly", Level = LogLevel.Information, Message = "Admin token created, it is only in the console output")]
    public static partial void AdminTokenCreated(this ILogger logger);
#pragma warning restore SYSLIB1025

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Note: {Key}={Given} is ignored, the stored value {Stored} applies (change it in the administration under Server)")]
    public static partial void StartValueIgnored(this ILogger logger, string key, string given, string stored);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} unused guest records removed (guest only, no ban, not logged in for over {Days:0} days)")]
    public static partial void GuestsPruned(this ILogger logger, int count, double days);

    /// <summary>Package 143 (A153): the exception follows the message in the file, with its stack trace.</summary>
    [LoggerMessage(Level = LogLevel.Error, Message = "Error in request {Request} from {Nickname}, error ID {ErrorId}:")]
    public static partial void RequestFailed(this ILogger logger, string request, string nickname, string errorId, Exception exception);

    // Saving (SaveScheduler)

    [LoggerMessage(Level = LogLevel.Information, Message = "Server data is being saved again")]
    public static partial void SavingAgain(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Server data not saved, retrying every {Seconds:0} s: {Reason}")]
    public static partial void SaveFailedRetrying(this ILogger logger, double seconds, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Server data not saved on shutdown: {Reason}")]
    public static partial void SaveOnCloseFailed(this ILogger logger, string reason);

    // Connections

    [LoggerMessage(Level = LogLevel.Information, Message = "Server password hash switched to PBKDF2")]
    public static partial void PasswordHashUpgraded(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "New identity from {Ip} refused: too many new identities ({PerHour} per hour)")]
    public static partial void NewIdentityRefused(this ILogger logger, IPAddress ip, int perHour);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Nickname} connected ({Fingerprint}, {Ip})")]
    public static partial void Connected(this ILogger logger, string nickname, string fingerprint, IPAddress ip);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Nickname} disconnected ({Reason})")]
    public static partial void Disconnected(this ILogger logger, string nickname, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Reason}, {Count} users disconnected")]
    public static partial void AllDisconnected(this ILogger logger, string reason, int count);

    /// <summary>Who is empty, or the nickname and fingerprint in brackets with a leading space.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "Connection from {Ip} refused: {Code}{Who}")]
    public static partial void ConnectionRefused(this ILogger logger, IPAddress ip, string code, string who);

    // Administration

    [LoggerMessage(Level = LogLevel.Warning, Message = "Wrong admin token from {Nickname} ({Ip})")]
    public static partial void AdminTokenWrong(this ILogger logger, string nickname, IPAddress ip);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Nickname} redeemed the admin token")]
    public static partial void AdminTokenRedeemed(this ILogger logger, string nickname);

    [LoggerMessage(Level = LogLevel.Information, Message = "Group '{Group}' created by {Nickname} ({Permissions})")]
    public static partial void GroupCreated(this ILogger logger, string group, string nickname, Permission permissions);

    [LoggerMessage(Level = LogLevel.Information, Message = "Groups reordered by {Nickname}: {Order}")]
    public static partial void GroupsReordered(this ILogger logger, string nickname, string order);

    [LoggerMessage(Level = LogLevel.Information, Message = "Group '{Group}' changed by {Nickname}: {Changes}")]
    public static partial void GroupChanged(this ILogger logger, string group, string nickname, string changes);

    [LoggerMessage(Level = LogLevel.Information, Message = "Group '{Group}' deleted by {Nickname}")]
    public static partial void GroupDeleted(this ILogger logger, string group, string nickname);

    [LoggerMessage(Level = LogLevel.Information, Message = "Group '{Group}' given to {User} by {Nickname}")]
    public static partial void GroupAssigned(this ILogger logger, string group, string user, string nickname);

    [LoggerMessage(Level = LogLevel.Information, Message = "Group '{Group}' taken from {User} by {Nickname}")]
    public static partial void GroupRevoked(this ILogger logger, string group, string user, string nickname);

    [LoggerMessage(Level = LogLevel.Information, Message = "Server settings changed by {Nickname}: {Changes}")]
    public static partial void ServerSettingsChanged(this ILogger logger, string nickname, string changes);

    [LoggerMessage(Level = LogLevel.Information, Message = "Server logo removed by {Nickname}")]
    public static partial void ServerIconRemoved(this ILogger logger, string nickname);

    [LoggerMessage(Level = LogLevel.Information, Message = "Server logo changed by {Nickname} ({Kilobytes} KB)")]
    public static partial void ServerIconChanged(this ILogger logger, string nickname, int kilobytes);

    // Backups

    [LoggerMessage(Level = LogLevel.Information, Message = "Backup {File} created by {Nickname}")]
    public static partial void BackupCreated(this ILogger logger, string file, string nickname);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Backup creation refused ({Nickname}), backup limit reached: {Reason}")]
    public static partial void BackupCreateOverLimit(this ILogger logger, string nickname, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Creating a backup failed ({Nickname}): {Reason}")]
    public static partial void BackupCreateFailed(this ILogger logger, string nickname, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Backup {File} deleted by {Nickname}")]
    public static partial void BackupDeleted(this ILogger logger, string file, string nickname);

    [LoggerMessage(Level = LogLevel.Information, Message = "Backup {File} being downloaded by {Nickname}")]
    public static partial void BackupDownloadStarted(this ILogger logger, string file, string nickname);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Backup upload from {Nickname} refused: larger than {Megabytes} MB")]
    public static partial void BackupUploadTooLarge(this ILogger logger, string nickname, long megabytes);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Backup upload from {Nickname} refused, backup limit reached: {Reason}")]
    public static partial void BackupUploadOverLimit(this ILogger logger, string nickname, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Backup upload from {Nickname} failed: {Reason}")]
    public static partial void BackupUploadFailed(this ILogger logger, string nickname, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Uploaded backup is invalid ({Nickname}): {Reason}")]
    public static partial void UploadedBackupInvalid(this ILogger logger, string nickname, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Backup {File} uploaded by {Nickname}")]
    public static partial void BackupUploaded(this ILogger logger, string file, string nickname);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Backup {File} is invalid ({Nickname}): {Reason}")]
    public static partial void BackupInvalid(this ILogger logger, string file, string nickname, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Safety backup not possible, backup limit reached, no restore ({Nickname}): {Reason}")]
    public static partial void SafetyBackupOverLimit(this ILogger logger, string nickname, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Safety backup failed, no restore ({Nickname}): {Reason}")]
    public static partial void SafetyBackupFailed(this ILogger logger, string nickname, string reason);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Backup {File} being restored by {Nickname} (data version {DataVersion}, server version {ServerVersion}), safety backup {SafetyBackup}")]
    public static partial void RestoreStarted(this ILogger logger, string file, string nickname, int dataVersion, string serverVersion, string safetyBackup);

    // Channels and chat

    [LoggerMessage(Level = LogLevel.Information, Message = "Channel '{Channel}' created by {Nickname}")]
    public static partial void ChannelCreated(this ILogger logger, string channel, string nickname);

    [LoggerMessage(Level = LogLevel.Information, Message = "Separator created by {Nickname}")]
    public static partial void SeparatorCreated(this ILogger logger, string nickname);

    [LoggerMessage(Level = LogLevel.Information, Message = "Channel '{Channel}' deleted by {Nickname}")]
    public static partial void ChannelDeleted(this ILogger logger, string channel, string nickname);

    [LoggerMessage(Level = LogLevel.Information, Message = "Separator deleted by {Nickname}")]
    public static partial void SeparatorDeleted(this ILogger logger, string nickname);

    [LoggerMessage(Level = LogLevel.Information, Message = "Channels reordered by {Nickname}: {Order}")]
    public static partial void ChannelsReordered(this ILogger logger, string nickname, string order);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Wrong channel password from {Nickname} for '{Channel}' (attempt {Attempt})")]
    public static partial void WrongChannelPassword(this ILogger logger, string nickname, string channel, int attempt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Wrong channel password from {Nickname} for '{Channel}' (attempt {Attempt}), blocked for {Minutes:0} minutes")]
    public static partial void WrongChannelPasswordBlocked(this ILogger logger, string nickname, string channel, int attempt, double minutes);

    [LoggerMessage(Level = LogLevel.Information, Message = "Links changed by {Nickname}: {Added} set, {Removed} removed")]
    public static partial void LinksChanged(this ILogger logger, string nickname, int added, int removed);

    [LoggerMessage(Level = LogLevel.Information, Message = "Chat from {Nickname}: {Text}")]
    public static partial void ServerChat(this ILogger logger, string nickname, string text);

    /// <summary>Never the content (A29).</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "{Nickname} writes privately to {Recipient}")]
    public static partial void PrivateChat(this ILogger logger, string nickname, string recipient);

    // Logs

    [LoggerMessage(Level = LogLevel.Information, Message = "Logs searched by {Nickname}")]
    public static partial void LogsSearched(this ILogger logger, string nickname);

    [LoggerMessage(Level = LogLevel.Information, Message = "Logs downloaded by {Nickname}: {File} ({Count} files, {Size} bytes)")]
    public static partial void LogsDownloaded(this ILogger logger, string nickname, string file, int count, long size);

    // Moderation

    [LoggerMessage(Level = LogLevel.Information, Message = "{Nickname} was kicked by {Moderator}: {Reason}")]
    public static partial void Kicked(this ILogger logger, string nickname, string moderator, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "User data of {User} deleted by {Nickname} ({Fingerprint}, {Bans} bans)")]
    public static partial void UserDataDeleted(this ILogger logger, string user, string nickname, string fingerprint, int bans);

    /// <summary>Duration is "for N minutes" or "permanently"; details are empty, or " with IP" and " (offline)".</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "{Nickname} was banned by {Moderator} {Duration}{Details}: {Reason}")]
    public static partial void Banned(this ILogger logger, string nickname, string moderator, string duration, string details, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Ban of {Nickname} lifted by {Moderator}")]
    public static partial void Unbanned(this ILogger logger, string nickname, string moderator);

    /// <summary>Change is "server-muted" or "server-unmuted"; offline is empty, or " (offline)".</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "{Nickname} {Change} by {Moderator}{Offline}")]
    public static partial void ServerMuteChanged(this ILogger logger, string nickname, string change, string moderator, string offline);
}
