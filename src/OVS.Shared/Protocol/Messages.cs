using System.Text.Json.Serialization;
using OVS.Shared.Permissions;

namespace OVS.Shared.Protocol;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
// Connection
[JsonDerivedType(typeof(Ping), "ping")]
[JsonDerivedType(typeof(Pong), "pong")]
[JsonDerivedType(typeof(Error), "error")]
[JsonDerivedType(typeof(ClientHello), "clientHello")]
[JsonDerivedType(typeof(Challenge), "challenge")]
[JsonDerivedType(typeof(ClientProof), "clientProof")]
[JsonDerivedType(typeof(Welcome), "welcome")]
[JsonDerivedType(typeof(Rejected), "rejected")]
[JsonDerivedType(typeof(Disconnected), "disconnected")]
// Channels and users
[JsonDerivedType(typeof(JoinChannel), "joinChannel")]
[JsonDerivedType(typeof(CreateChannel), "createChannel")]
[JsonDerivedType(typeof(EditChannel), "editChannel")]
[JsonDerivedType(typeof(ReorderChannels), "reorderChannels")]
[JsonDerivedType(typeof(DeleteChannel), "deleteChannel")]
[JsonDerivedType(typeof(MoveUser), "moveUser")]
[JsonDerivedType(typeof(SetSelfState), "setSelfState")]
[JsonDerivedType(typeof(ChannelAdded), "channelAdded")]
[JsonDerivedType(typeof(ChannelUpdated), "channelUpdated")]
[JsonDerivedType(typeof(ChannelRemoved), "channelRemoved")]
[JsonDerivedType(typeof(UserJoined), "userJoined")]
[JsonDerivedType(typeof(UserUpdated), "userUpdated")]
[JsonDerivedType(typeof(UserLeft), "userLeft")]
// Administration
[JsonDerivedType(typeof(CreateGroup), "createGroup")]
[JsonDerivedType(typeof(UpdateGroup), "updateGroup")]
[JsonDerivedType(typeof(ReorderGroups), "reorderGroups")]
[JsonDerivedType(typeof(DeleteGroup), "deleteGroup")]
[JsonDerivedType(typeof(AssignGroup), "assignGroup")]
[JsonDerivedType(typeof(UnassignGroup), "unassignGroup")]
[JsonDerivedType(typeof(ListUsers), "listUsers")]
[JsonDerivedType(typeof(UserList), "userList")]
[JsonDerivedType(typeof(RedeemAdminToken), "redeemAdminToken")]
[JsonDerivedType(typeof(UpdateServerSettings), "updateServerSettings")]
[JsonDerivedType(typeof(GroupsChanged), "groupsChanged")]
[JsonDerivedType(typeof(ServerSettingsChanged), "serverSettingsChanged")]
[JsonDerivedType(typeof(SetServerIcon), "setServerIcon")]
[JsonDerivedType(typeof(GetServerIcon), "getServerIcon")]
[JsonDerivedType(typeof(ServerIcon), "serverIcon")]
// Backups (Package 74)
[JsonDerivedType(typeof(ListBackups), "listBackups")]
[JsonDerivedType(typeof(CreateBackup), "createBackup")]
[JsonDerivedType(typeof(DeleteBackup), "deleteBackup")]
[JsonDerivedType(typeof(RestoreBackup), "restoreBackup")]
[JsonDerivedType(typeof(BackupList), "backupList")]
[JsonDerivedType(typeof(DownloadBackup), "downloadBackup")] // Package 75
[JsonDerivedType(typeof(BackupChunk), "backupChunk")]
[JsonDerivedType(typeof(UploadBackupChunk), "uploadBackupChunk")]
[JsonDerivedType(typeof(UploadBackupAck), "uploadBackupAck")]
[JsonDerivedType(typeof(BackupUploaded), "backupUploaded")]
// Logs (Package 81)
[JsonDerivedType(typeof(ListLogs), "listLogs")]
[JsonDerivedType(typeof(LogList), "logList")]
[JsonDerivedType(typeof(ReadLog), "readLog")]
[JsonDerivedType(typeof(LogPage), "logPage")]
[JsonDerivedType(typeof(SearchLogs), "searchLogs")]
[JsonDerivedType(typeof(LogSearchResult), "logSearchResult")]
[JsonDerivedType(typeof(PrepareLogDownload), "prepareLogDownload")] // Package 82
[JsonDerivedType(typeof(LogDownloadReady), "logDownloadReady")]
[JsonDerivedType(typeof(DownloadLogChunk), "downloadLogChunk")]
[JsonDerivedType(typeof(LogChunk), "logChunk")]
// Moderation
[JsonDerivedType(typeof(Kick), "kick")]
[JsonDerivedType(typeof(Ban), "ban")]
[JsonDerivedType(typeof(Unban), "unban")]
[JsonDerivedType(typeof(BanUser), "banUser")]
[JsonDerivedType(typeof(DeleteUser), "deleteUser")]
[JsonDerivedType(typeof(ListBans), "listBans")]
[JsonDerivedType(typeof(BanList), "banList")]
[JsonDerivedType(typeof(SetServerMute), "setServerMute")]
[JsonDerivedType(typeof(SetStoredServerMute), "setStoredServerMute")] // Package 85
// Links
[JsonDerivedType(typeof(LinkChannels), "linkChannels")]
[JsonDerivedType(typeof(SetChannelLinks), "setChannelLinks")]
[JsonDerivedType(typeof(UnlinkChannels), "unlinkChannels")]
[JsonDerivedType(typeof(ChannelsLinked), "channelsLinked")]
[JsonDerivedType(typeof(ChannelsUnlinked), "channelsUnlinked")]
// Chat
[JsonDerivedType(typeof(SendChat), "sendChat")]
[JsonDerivedType(typeof(ChatMessage), "chatMessage")]
public abstract record Message;

/// <summary>Client request. A failed request is answered with an Error carrying the same RequestId.</summary>
public abstract record Request : Message
{
    public string? RequestId { get; init; }
}

// ---- Connection ----
public sealed record Ping : Message;
public sealed record Pong : Message;
public sealed record Error(string? RequestId, string Code, string? Detail = null) : Message;
public sealed record ClientHello(int ProtocolVersion, string Nickname, string PublicKey, string? Password) : Message;
public sealed record Challenge(string Nonce) : Message;
public sealed record ClientProof(string Signature) : Message;
public sealed record Welcome(uint SessionId, string VoiceKey, ServerSnapshot Snapshot) : Message;
public sealed record Rejected(string Code, string? Detail = null) : Message;
public sealed record Disconnected(string Reason, string? Detail = null) : Message;

// ---- State ----
/// <param name="IconHash">Hash of the server logo (ServerIconFormat.Hash), null when the server has none.</param>
/// <param name="Limits">Package 69: only sent to clients with the ServerConfig right, null for everyone else.</param>
public sealed record ServerSettingsInfo(string Name, string WelcomeText, bool HasPassword, string? IconHash = null, ServerLimits? Limits = null);
/// <summary>Package 69: settings that used to be environment variables, now changed in the administration.</summary>
/// <param name="MaxUsers">1 to 100000.</param>
/// <param name="LogDays">0 to 3650, 0 keeps every log file.</param>
/// <param name="AutoRestartTime">Daily restart time in server local time, used while AutoRestart is on.</param>
public sealed record ServerLimits(int MaxUsers, int LogDays, bool LogRotateDaily, bool AutoRestart, TimeOnly AutoRestartTime);
/// <param name="IsMuted">Package 34: nobody in this channel is heard, not even via link.</param>
/// <param name="MaxUsers">Package 35: 0 = unlimited.</param>
/// <param name="AllowedGroupIds">Package 93: null = no group lock; else only members of one of these groups (and admins) may
/// join, an empty list (all its groups deleted) means admins only.</param>
/// <param name="HasPassword">Package 94: joining needs the password (the password itself never leaves the server).</param>
public sealed record ChannelInfo(Guid Id, string Name, string Description, int Order, bool IsMuted = false, int MaxUsers = 0,
    IReadOnlyList<Guid>? AllowedGroupIds = null, bool HasPassword = false);
public sealed record LinkInfo(Guid A, Guid B);
/// <param name="Permissions">Package 92 (A104): only for recipients with GroupsView, None for everyone else.</param>
/// <param name="AssignableByMe">Package 92: the recipient may assign this group (GroupsAssign and not stronger than the recipient).</param>
public sealed record GroupInfo(Guid Id, string Name, Permission Permissions, bool AssignableByMe = false);
/// <param name="Fingerprint">Package 92 (A104): public data, needed for private chat and the volume per person.</param>
/// <param name="Permissions">Package 92: only in the recipient's own entry, None for everyone else.</param>
/// <param name="CanBeModeratedByMe">Package 92: the recipient may act on this user (Package 84 rank rule, A102).</param>
public sealed record UserInfo(
    uint SessionId, string Fingerprint, string Nickname, Guid ChannelId,
    bool SelfMuted, bool SelfDeafened, bool ServerMuted,
    Permission Permissions, IReadOnlyList<Guid> GroupIds, bool CanBeModeratedByMe = false);
public sealed record ServerSnapshot(
    ServerSettingsInfo Settings, Guid DefaultChannelId,
    IReadOnlyList<ChannelInfo> Channels, IReadOnlyList<LinkInfo> Links,
    IReadOnlyList<GroupInfo> Groups, IReadOnlyList<UserInfo> Users);

// ---- Channels and users ----
/// <param name="Password">Package 94: for a password-locked channel.</param>
public sealed record JoinChannel(Guid ChannelId, string? Password = null) : Request;
/// <summary>Package 54: created with its options right away, like <see cref="EditChannel"/>.</summary>
/// <param name="AllowedGroupIds">Package 93: the group lock, null or empty = none.</param>
/// <param name="Password">Package 94: null or empty = none, else 1 to MaxPasswordLength characters.</param>
public sealed record CreateChannel(string Name, string Description, bool IsMuted = false, int MaxUsers = 0,
    IReadOnlyList<Guid>? AllowedGroupIds = null, string? Password = null) : Request;
/// <param name="AllowedGroupIds">Package 93: null = unchanged, empty = remove the group lock.</param>
/// <param name="Password">Package 94: null = unchanged, empty = remove, else the new password.</param>
public sealed record EditChannel(Guid ChannelId, string Name, string Description, int Order, bool IsMuted = false, int MaxUsers = 0,
    IReadOnlyList<Guid>? AllowedGroupIds = null, string? Password = null) : Request;
/// <summary>Package 36: the complete new order, every channel exactly once.</summary>
public sealed record ReorderChannels(IReadOnlyList<Guid> ChannelIds) : Request;
public sealed record DeleteChannel(Guid ChannelId) : Request;
public sealed record MoveUser(uint SessionId, Guid ChannelId) : Request;
public sealed record SetSelfState(bool Muted, bool Deafened) : Request;
public sealed record ChannelAdded(ChannelInfo Channel) : Message;
public sealed record ChannelUpdated(ChannelInfo Channel) : Message;
public sealed record ChannelRemoved(Guid ChannelId) : Message;
public sealed record UserJoined(UserInfo User) : Message;
public sealed record UserUpdated(UserInfo User) : Message;
public sealed record UserLeft(uint SessionId) : Message;

// ---- Administration ----
/// <summary>A user the server has seen. Package 70: with statistics; for online users they include the running session.</summary>
/// <param name="LastLogin">null when unknown (before Package 70 or never logged in).</param>
/// <param name="LastIp">A personal datum, only in the user list.</param>
/// <param name="PreviousNicknames">Newest first, at most 5.</param>
/// <param name="SessionId">Set while the user is online.</param>
public sealed record KnownUserInfo(string Fingerprint, string LastNickname, IReadOnlyList<Guid> GroupIds,
    DateTimeOffset FirstSeen = default, DateTimeOffset? LastLogin = null, int LoginCount = 0, TimeSpan OnlineTime = default,
    string? LastIp = null, IReadOnlyList<string>? PreviousNicknames = null, TimeSpan SpeechTime = default, int ChatMessages = 0,
    bool IsOnline = false, uint? SessionId = null, IReadOnlyList<BanInfo>? Bans = null, // Package 71: the active bans on this fingerprint
    bool ServerMuted = false, // Package 85: the stored server mute, applied on every login
    bool CanBeModeratedByMe = false); // Package 92: the requester may act on this user (A102), as the server checks it
public sealed record CreateGroup(string Name, Permission Permissions) : Request;
public sealed record UpdateGroup(Guid GroupId, string Name, Permission Permissions) : Request;
/// <summary>Package 37: the complete new group order, every group exactly once. Display only, the rank stays with the rights.</summary>
public sealed record ReorderGroups(IReadOnlyList<Guid> GroupIds) : Request;
public sealed record DeleteGroup(Guid GroupId) : Request;
public sealed record AssignGroup(string Fingerprint, Guid GroupId) : Request;
public sealed record UnassignGroup(string Fingerprint, Guid GroupId) : Request;
/// <param name="Offset">Package 87: every list comes in pages; Offset is the first entry wanted, the server picks the page size.</param>
public sealed record ListUsers(int Offset = 0) : Request;
/// <param name="Offset">Package 87: the position of the first entry in the whole list.</param>
/// <param name="Total">Package 87: entries in the whole list; more pages follow while Offset + Users.Count is below it.</param>
public sealed record UserList(string? RequestId, IReadOnlyList<KnownUserInfo> Users, int Offset = 0, int Total = 0) : Message;
public sealed record RedeemAdminToken(string Token) : Request;
/// <param name="Password">null = unchanged, "" = remove, anything else = new password.</param>
/// <param name="Limits">Package 69: null = unchanged.</param>
public sealed record UpdateServerSettings(string Name, string WelcomeText, string? Password, ServerLimits? Limits = null) : Request;
public sealed record GroupsChanged(IReadOnlyList<GroupInfo> Groups) : Message;
public sealed record ServerSettingsChanged(ServerSettingsInfo Settings) : Message;
/// <summary>Sets the server logo, a square PNG as base64 (see ServerIconFormat); null removes it.</summary>
public sealed record SetServerIcon(string? PngBase64) : Request;
/// <summary>Asks for the logo; clients only do this when the hash differs from their cache.</summary>
public sealed record GetServerIcon : Request;
public sealed record ServerIcon(string? RequestId, string? Hash, string? PngBase64) : Message;

// ---- Backups (Package 74, right ServerConfig) ----
/// <param name="FileName">Only the name inside the server's backups folder; requests name a backup by it.</param>
public sealed record BackupInfo(string FileName, DateTimeOffset CreatedAt, long Size, string ServerVersion);
/// <param name="Offset">Package 87: paged like <see cref="ListUsers"/>.</param>
public sealed record ListBackups(int Offset = 0) : Request;
/// <summary>Answered with the new BackupList.</summary>
public sealed record CreateBackup : Request;
/// <summary>Answered with the new BackupList.</summary>
public sealed record DeleteBackup(string FileName) : Request;
/// <summary>Takes a safety backup, disconnects everyone with Restoring and restarts the server on the backup's state.</summary>
public sealed record RestoreBackup(string FileName) : Request;
/// <summary>Newest first.</summary>
public sealed record BackupList(string? RequestId, IReadOnlyList<BackupInfo> Backups, int Offset = 0, int Total = 0) : Message;
// Package 75 (A91): transfers in chunks of ProtocolInfo.BackupChunkBytes, always one request per chunk, so the outbox never fills up
/// <summary>Asks for the chunk at Offset; answered with one BackupChunk.</summary>
public sealed record DownloadBackup(string FileName, long Offset) : Request;
public sealed record BackupChunk(string? RequestId, string FileName, long Offset, long TotalSize, string DataBase64, bool IsLast) : Message;
/// <param name="UploadId">32 lowercase hex digits chosen by the client; a new id at offset 0 replaces an unfinished upload.</param>
/// <summary>Answered with UploadBackupAck, the last one (IsLast) with BackupUploaded and the new BackupList once the archive is valid.</summary>
public sealed record UploadBackupChunk(string UploadId, long Offset, string DataBase64, bool IsLast) : Request;
/// <param name="Received">The bytes stored so far, the offset of the next chunk.</param>
public sealed record UploadBackupAck(string? RequestId, string UploadId, long Received) : Message;
public sealed record BackupUploaded(string? RequestId, BackupInfo Backup) : Message;

// ---- Logs (Package 81, A98: right LogsView) ----
public enum LogKind { Server, Channel }
/// <param name="Id">The file's name relative to the server's logs folder ("server/..." or "channels/&lt;id&gt;/..."); requests name a file only by it.</param>
/// <param name="ChannelName">The channel's current name, or the one in the file for a deleted channel; null for server logs.</param>
/// <param name="Start">The start in the file name; LastWrite is the time of its last line.</param>
public sealed record LogFileInfo(string Id, LogKind Kind, Guid? ChannelId, string? ChannelName, DateTimeOffset Start, DateTimeOffset LastWrite, long Size);
/// <param name="Offset">Package 87: paged like <see cref="ListUsers"/>.</param>
public sealed record ListLogs(int Offset = 0) : Request;
/// <summary>Newest first.</summary>
public sealed record LogList(string? RequestId, IReadOnlyList<LogFileInfo> Files, int Offset = 0, int Total = 0) : Message;
/// <summary>Asks for one page of ProtocolInfo.LogPageLines lines; Page null = the last page.</summary>
public sealed record ReadLog(string FileId, int? Page = null) : Request;
/// <param name="Page">1-based, of PageCount.</param>
/// <param name="FirstLine">The 1-based number of Lines[0] in the file.</param>
public sealed record LogPage(string? RequestId, string FileId, int Page, int PageCount, int FirstLine, IReadOnlyList<string> Lines) : Message;
/// <summary>Plain text, case-insensitive, over every file that passes the optional filters (Kind, ChannelId, period of the file).</summary>
public sealed record SearchLogs(string Query, LogKind? Kind = null, Guid? ChannelId = null, DateTimeOffset? From = null, DateTimeOffset? To = null) : Request;
/// <param name="Line">1-based line number in the file.</param>
public sealed record LogHit(string FileId, int Line, string Text);
/// <summary>Newest first. Truncated: there were more than ProtocolInfo.MaxLogHits; TimedOut: the search stopped after its time limit.</summary>
public sealed record LogSearchResult(string? RequestId, IReadOnlyList<LogHit> Hits, bool Truncated, bool TimedOut) : Message;
// Package 82 (A99, rights LogsView and LogsDownload): a snapshot on the server, then pulled in chunks like a backup
/// <summary>One file comes as .log, several as one zip (server/..., channels/&lt;name&gt;_&lt;id&gt;/...). Answered with LogDownloadReady.</summary>
public sealed record PrepareLogDownload(IReadOnlyList<string> FileIds) : Request;
/// <param name="DownloadId">Names the snapshot in DownloadLogChunk; one per session, a new one replaces it.</param>
/// <param name="FileName">The file's own name, or ovs-logs_&lt;from&gt;_&lt;to&gt;.zip.</param>
public sealed record LogDownloadReady(string? RequestId, string DownloadId, string FileName, long Size) : Message;
/// <summary>Asks for the chunk at Offset; answered with one LogChunk. The snapshot is deleted after the last one.</summary>
public sealed record DownloadLogChunk(string DownloadId, long Offset) : Request;
public sealed record LogChunk(string? RequestId, string DownloadId, long Offset, long TotalSize, string DataBase64, bool IsLast) : Message;

// ---- Moderation ----
/// <summary>Package 80 (A97): active, expired and lifted bans; the new values are null or 0 for bans saved before.</summary>
public sealed record BanInfo(
    Guid Id, string Fingerprint, string Nickname, string? Ip, string Reason, string CreatedBy, DateTimeOffset? ExpiresAt,
    DateTimeOffset? CreatedAt = null, string? CreatedByFingerprint = null, int? DurationMinutes = null,
    DateTimeOffset? LiftedAt = null, string? LiftedBy = null, int BlockedAttempts = 0, DateTimeOffset? LastAttempt = null, string? LastAttemptIp = null);
public sealed record Kick(uint SessionId, string Reason) : Request;
public sealed record Ban(uint SessionId, string Reason, int? DurationMinutes, bool IncludeIp) : Request;
public sealed record Unban(Guid BanId) : Request;
/// <summary>Package 72: bans a known user by fingerprint, online or offline; IncludeIp uses the last known IP.</summary>
public sealed record BanUser(string Fingerprint, string Reason, int? DurationMinutes, bool IncludeIp) : Request;
/// <summary>Package 72: removes the user record and every ban on this fingerprint (A88).</summary>
public sealed record DeleteUser(string Fingerprint) : Request;
/// <param name="Offset">Package 87: paged like <see cref="ListUsers"/>.</param>
public sealed record ListBans(int Offset = 0) : Request;
public sealed record BanList(string? RequestId, IReadOnlyList<BanInfo> Bans, int Offset = 0, int Total = 0) : Message;
public sealed record SetServerMute(uint SessionId, bool Muted) : Request;
/// <summary>Package 85: the stored server mute of a known user by fingerprint, online or offline; right UserMute and the rank rule.</summary>
public sealed record SetStoredServerMute(string Fingerprint, bool Muted) : Request;

// ---- Links ----
public sealed record LinkChannels(Guid A, Guid B) : Request;
/// <summary>Package 38: many links at once, all or nothing.</summary>
public sealed record SetChannelLinks(IReadOnlyList<LinkInfo> Add, IReadOnlyList<LinkInfo> Remove) : Request;
public sealed record UnlinkChannels(Guid A, Guid B) : Request;
public sealed record ChannelsLinked(Guid A, Guid B) : Message;
public sealed record ChannelsUnlinked(Guid A, Guid B) : Message;

// ---- Chat (Package 31) ----
public enum ChatTarget { Server, Channel, Private }

/// <summary>Server: everyone. Channel: the sender's current channel (not linked ones). Private: exactly one online user.</summary>
public sealed record SendChat(ChatTarget Target, uint? ToSessionId, string Text) : Request;

/// <summary>A delivered chat message; the sender gets it back as well, with the server's time.</summary>
public sealed record ChatMessage(
    ChatTarget Target, uint FromSessionId, string FromNickname, Guid? ChannelId, uint? ToSessionId, string Text, DateTimeOffset SentAt) : Message;
