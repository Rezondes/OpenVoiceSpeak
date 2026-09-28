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
// Moderation
[JsonDerivedType(typeof(Kick), "kick")]
[JsonDerivedType(typeof(Ban), "ban")]
[JsonDerivedType(typeof(Unban), "unban")]
[JsonDerivedType(typeof(ListBans), "listBans")]
[JsonDerivedType(typeof(BanList), "banList")]
[JsonDerivedType(typeof(SetServerMute), "setServerMute")]
// Links
[JsonDerivedType(typeof(LinkChannels), "linkChannels")]
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
public sealed record ServerSettingsInfo(string Name, string WelcomeText, bool HasPassword, string? IconHash = null);
/// <param name="IsMuted">Package 34: nobody in this channel is heard, not even via link.</param>
public sealed record ChannelInfo(Guid Id, string Name, string Description, int Order, bool IsMuted = false);
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
public sealed record CreateChannel(string Name, string Description) : Request;
public sealed record EditChannel(Guid ChannelId, string Name, string Description, int Order, bool IsMuted = false) : Request;
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
public sealed record KnownUserInfo(string Fingerprint, string LastNickname, IReadOnlyList<Guid> GroupIds);
public sealed record CreateGroup(string Name, Permission Permissions) : Request;
public sealed record UpdateGroup(Guid GroupId, string Name, Permission Permissions) : Request;
public sealed record DeleteGroup(Guid GroupId) : Request;
public sealed record AssignGroup(string Fingerprint, Guid GroupId) : Request;
public sealed record UnassignGroup(string Fingerprint, Guid GroupId) : Request;
public sealed record ListUsers : Request;
public sealed record UserList(string? RequestId, IReadOnlyList<KnownUserInfo> Users) : Message;
public sealed record RedeemAdminToken(string Token) : Request;
/// <param name="Password">null = unchanged, "" = remove, anything else = new password.</param>
public sealed record UpdateServerSettings(string Name, string WelcomeText, string? Password) : Request;
public sealed record GroupsChanged(IReadOnlyList<GroupInfo> Groups) : Message;
public sealed record ServerSettingsChanged(ServerSettingsInfo Settings) : Message;
/// <summary>Sets the server logo, a square PNG as base64 (see ServerIconFormat); null removes it.</summary>
public sealed record SetServerIcon(string? PngBase64) : Request;
/// <summary>Asks for the logo; clients only do this when the hash differs from their cache.</summary>
public sealed record GetServerIcon : Request;
public sealed record ServerIcon(string? RequestId, string? Hash, string? PngBase64) : Message;

// ---- Moderation ----
public sealed record BanInfo(
    Guid Id, string Fingerprint, string Nickname, string? Ip, string Reason, string CreatedBy, DateTimeOffset? ExpiresAt);
public sealed record Kick(uint SessionId, string Reason) : Request;
public sealed record Ban(uint SessionId, string Reason, int? DurationMinutes, bool IncludeIp) : Request;
public sealed record Unban(Guid BanId) : Request;
public sealed record ListBans : Request;
public sealed record BanList(string? RequestId, IReadOnlyList<BanInfo> Bans) : Message;
public sealed record SetServerMute(uint SessionId, bool Muted) : Request;

// ---- Links ----
public sealed record LinkChannels(Guid A, Guid B) : Request;
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
