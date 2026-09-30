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
// Moderation
[JsonDerivedType(typeof(Kick), "kick")]
[JsonDerivedType(typeof(Ban), "ban")]
[JsonDerivedType(typeof(Unban), "unban")]
[JsonDerivedType(typeof(BanUser), "banUser")]
[JsonDerivedType(typeof(DeleteUser), "deleteUser")]
[JsonDerivedType(typeof(ListBans), "listBans")]
[JsonDerivedType(typeof(BanList), "banList")]
[JsonDerivedType(typeof(SetServerMute), "setServerMute")]
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
public sealed record ChannelInfo(Guid Id, string Name, string Description, int Order, bool IsMuted = false, int MaxUsers = 0);
public sealed record LinkInfo(Guid A, Guid B);
public sealed record GroupInfo(Guid Id, string Name, Permission Permissions);
public sealed record UserInfo(
    uint SessionId, string Fingerprint, string Nickname, Guid ChannelId,
    bool SelfMuted, bool SelfDeafened, bool ServerMuted,
    Permission Permissions, IReadOnlyList<Guid> GroupIds);
public sealed record ServerSnapshot(
    ServerSettingsInfo Settings, Guid DefaultChannelId,
    IReadOnlyList<ChannelInfo> Channels, IReadOnlyList<LinkInfo> Links,
    IReadOnlyList<GroupInfo> Groups, IReadOnlyList<UserInfo> Users);

// ---- Channels and users ----
public sealed record JoinChannel(Guid ChannelId) : Request;
/// <summary>Package 54: created with its options right away, like <see cref="EditChannel"/>.</summary>
public sealed record CreateChannel(string Name, string Description, bool IsMuted = false, int MaxUsers = 0) : Request;
public sealed record EditChannel(Guid ChannelId, string Name, string Description, int Order, bool IsMuted = false, int MaxUsers = 0) : Request;
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
    bool IsOnline = false, uint? SessionId = null, IReadOnlyList<BanInfo>? Bans = null); // Package 71: the active bans on this fingerprint
public sealed record CreateGroup(string Name, Permission Permissions) : Request;
public sealed record UpdateGroup(Guid GroupId, string Name, Permission Permissions) : Request;
/// <summary>Package 37: the complete new group order, every group exactly once. Display only, the rank stays with the rights.</summary>
public sealed record ReorderGroups(IReadOnlyList<Guid> GroupIds) : Request;
public sealed record DeleteGroup(Guid GroupId) : Request;
public sealed record AssignGroup(string Fingerprint, Guid GroupId) : Request;
public sealed record UnassignGroup(string Fingerprint, Guid GroupId) : Request;
public sealed record ListUsers : Request;
public sealed record UserList(string? RequestId, IReadOnlyList<KnownUserInfo> Users) : Message;
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
public sealed record ListBackups : Request;
/// <summary>Answered with the new BackupList.</summary>
public sealed record CreateBackup : Request;
/// <summary>Answered with the new BackupList.</summary>
public sealed record DeleteBackup(string FileName) : Request;
/// <summary>Takes a safety backup, disconnects everyone with Restoring and restarts the server on the backup's state.</summary>
public sealed record RestoreBackup(string FileName) : Request;
/// <summary>Newest first.</summary>
public sealed record BackupList(string? RequestId, IReadOnlyList<BackupInfo> Backups) : Message;
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

// ---- Moderation ----
public sealed record BanInfo(
    Guid Id, string Fingerprint, string Nickname, string? Ip, string Reason, string CreatedBy, DateTimeOffset? ExpiresAt);
public sealed record Kick(uint SessionId, string Reason) : Request;
public sealed record Ban(uint SessionId, string Reason, int? DurationMinutes, bool IncludeIp) : Request;
public sealed record Unban(Guid BanId) : Request;
/// <summary>Package 72: bans a known user by fingerprint, online or offline; IncludeIp uses the last known IP.</summary>
public sealed record BanUser(string Fingerprint, string Reason, int? DurationMinutes, bool IncludeIp) : Request;
/// <summary>Package 72: removes the user record and every ban on this fingerprint (A88).</summary>
public sealed record DeleteUser(string Fingerprint) : Request;
public sealed record ListBans : Request;
public sealed record BanList(string? RequestId, IReadOnlyList<BanInfo> Bans) : Message;
public sealed record SetServerMute(uint SessionId, bool Muted) : Request;

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
