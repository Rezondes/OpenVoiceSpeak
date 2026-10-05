using System.Text.Json.Serialization;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Chat;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.Groups;
using OVS.Shared.Protocol.Messages.Links;
using OVS.Shared.Protocol.Messages.LogAccess;
using OVS.Shared.Protocol.Messages.Moderation;
using OVS.Shared.Protocol.Messages.ServerBackups;
using OVS.Shared.Protocol.Messages.ServerInfo;
using OVS.Shared.Protocol.Messages.Users;
using OVS.Shared.Protocol.Messages.VoiceRelay;

namespace OVS.Shared.Protocol.Messages;

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
[JsonDerivedType(typeof(VoiceUp), "voiceUp")] // Package 141
[JsonDerivedType(typeof(VoiceDown), "voiceDown")]
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
