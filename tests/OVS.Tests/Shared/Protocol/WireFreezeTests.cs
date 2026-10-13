using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using OVS.Client.Settings;
using OVS.Server.Data;
using OVS.Server.Data.Records;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Framing;
using OVS.Shared.Protocol.Messages;

namespace OVS.Tests.Shared.Protocol;

/// <summary>
/// The wire and the persisted files carry no CLR type names, so renaming a type must change none of the names below:
/// the discriminators with the JSON property names of each message, the property names of the records the messages
/// carry, every code and the member names of every enum written as a string. A change here is a protocol change.
/// </summary>
public sealed class WireFreezeTests
{
    /// <summary>Discriminator: the JSON property names of its message, sorted.</summary>
    static readonly string[] Messages =
    [
        "assignGroup: fingerprint, groupId, requestId",
        "backupChunk: dataBase64, fileName, isLast, offset, requestId, totalSize",
        "backupList: backups, offset, requestId, total",
        "backupUploaded: backup, requestId",
        "ban: durationMinutes, includeIp, reason, requestId, sessionId",
        "banList: bans, offset, requestId, total",
        "banUser: durationMinutes, fingerprint, includeIp, reason, requestId",
        "challenge: nonce",
        "channelAdded: channel, requestId",
        "channelRemoved: channelId",
        "channelUpdated: channel",
        "channelsLinked: a, b",
        "channelsUnlinked: a, b",
        "chatMessage: channelId, fromNickname, fromSessionId, sentAt, target, text, toSessionId",
        "clientHello: nickname, password, protocolVersion, publicKey",
        "clientProof: signature",
        "createBackup: requestId",
        "createChannel: allowedGroupIds, bitrate, description, isMuted, kind, maxUsers, name, password, requestId",
        "createGroup: name, permissions, requestId",
        "deleteBackup: fileName, requestId",
        "deleteChannel: channelId, requestId",
        "deleteGroup: groupId, requestId",
        "deleteUser: fingerprint, requestId",
        "disconnected: detail, reason",
        "downloadBackup: fileName, offset, requestId",
        "downloadLogChunk: downloadId, offset, requestId",
        "editChannel: allowedGroupIds, bitrate, channelId, description, isMuted, maxUsers, name, order, password, requestId",
        "error: code, detail, requestId",
        "getServerIcon: requestId",
        "groupsChanged: groups",
        "joinChannel: channelId, password, requestId",
        "kick: reason, requestId, sessionId",
        "linkChannels: a, b, requestId",
        "listBackups: offset, requestId",
        "listBans: offset, requestId",
        "listLogs: offset, requestId",
        "listUsers: offset, requestId",
        "logChunk: dataBase64, downloadId, isLast, offset, requestId, totalSize",
        "logDownloadReady: downloadId, fileName, requestId, size",
        "logList: files, offset, requestId, total",
        "logPage: fileId, firstLine, lines, page, pageCount, requestId",
        "logSearchResult: hits, requestId, timedOut, truncated",
        "moveUser: channelId, requestId, sessionId",
        "ping:",
        "pong:",
        "prepareLogDownload: fileIds, requestId",
        "readLog: fileId, page, requestId",
        "redeemAdminToken: requestId, token",
        "rejected: code, detail",
        "reorderChannels: channelIds, requestId",
        "reorderGroups: groupIds, requestId",
        "restoreBackup: fileName, requestId",
        "searchLogs: channelId, from, kind, query, requestId, to",
        "sendChat: requestId, target, text, toSessionId",
        "serverIcon: hash, pngBase64, requestId",
        "serverSettingsChanged: settings",
        "setChannelLinks: add, remove, requestId",
        "setSelfState: deafened, muted, requestId",
        "setServerIcon: pngBase64, requestId",
        "setServerMute: muted, requestId, sessionId",
        "setStoredServerMute: fingerprint, muted, requestId",
        "unassignGroup: fingerprint, groupId, requestId",
        "unban: banId, requestId",
        "unlinkChannels: a, b, requestId",
        "updateGroup: groupId, name, permissions, requestId",
        "updateServerSettings: limits, name, password, requestId, welcomeText",
        "uploadBackupAck: received, requestId, uploadId",
        "uploadBackupChunk: dataBase64, isLast, offset, requestId, uploadId",
        "userJoined: user",
        "userLeft: sessionId",
        "userList: offset, requestId, total, users",
        "userUpdated: user",
        "voiceDown: opus, seq, sessionId, target",
        "voiceUp: opus, seq, target",
        "welcome: sessionId, snapshot, voiceKey",
    ];

    /// <summary>Record carried by a message: its JSON property names, sorted.</summary>
    static readonly string[] Records =
    [
        "BackupInfo: createdAt, fileName, serverVersion, size",
        "BanInfo: blockedAttempts, createdAt, createdBy, createdByFingerprint, durationMinutes, expiresAt, fingerprint, id, ip, lastAttempt, lastAttemptIp, liftedAt, liftedBy, nickname, reason",
        "ChannelInfo: allowedGroupIds, bitrate, description, hasPassword, id, isMuted, kind, maxUsers, name, order",
        "GroupInfo: assignableByMe, id, name, permissions",
        "KnownUserInfo: bans, canBeModeratedByMe, chatMessages, fingerprint, firstSeen, groupIds, isOnline, lastIp, lastLogin, lastNickname, loginCount, onlineTime, previousNicknames, serverMuted, sessionId, speechTime",
        "LinkInfo: a, b",
        "LogFileInfo: channelId, channelName, id, kind, lastWrite, size, start",
        "LogHit: fileId, line, text",
        "ServerLimits: autoRestart, autoRestartTime, logDays, logRotateDaily, maxUsers",
        "ServerSettingsInfo: hasPassword, iconHash, limits, name, welcomeText",
        "ServerSnapshot: channels, defaultChannelId, groups, links, settings, users",
        "UserInfo: canBeModeratedByMe, channelId, fingerprint, groupIds, nickname, permissions, selfDeafened, selfMuted, serverMuted, sessionId",
    ];

    static readonly string[] CodeValues =
    [
        "BackupQuotaExceeded",
        "BackupTooLarge",
        "BadSignature",
        "Banned",
        "CannotDeleteDefault",
        "ChannelFull",
        "ChannelLocked",
        "ChannelPasswordRequired",
        "ConnectionLost",
        "InvalidBackup",
        "InvalidLink",
        "InvalidName",
        "InvalidToken",
        "InvalidValue",
        "Kicked",
        "LastAdmin",
        "LogsTooLarge",
        "NameTaken",
        "NicknameInvalid",
        "NicknameTaken",
        "NotFound",
        "NotJoinable",
        "PermissionDenied",
        "ProtectedGroup",
        "ProtocolError",
        "RateLimited",
        "ReplacedByNewConnection",
        "Restoring",
        "ServerFull",
        "ServerRestart",
        "ServerShutdown",
        "Timeout",
        "TooManyConnections",
        "TooManyPasswordAttempts",
        "UnknownRequest",
        "UserDeleted",
        "VersionMismatch",
        "WrongChannelPassword",
        "WrongPassword",
    ];

    /// <summary>Enum on the wire or in a persisted file: its member names.</summary>
    static readonly string[] Enums =
    [
        "AppLanguage: System, German, English",
        "AppTheme: System, Light, Dark",
        "AudioContent: Voice, Music",
        "ChannelKind: Voice, Separator",
        "ChatTarget: Server, Channel, Private",
        "ChordModifiers: None, Ctrl, Shift, Alt",
        "DisplayMode: Animated, Simplified",
        "KeyAction: PushToTalk, LinkPushToTalk, PushToMute, ToggleMute, ToggleDeafen",
        "LogKind: Server, Channel",
        "Permission: None, Speak, SpeakLinked, ChannelCreate, ChannelEdit, ChannelDelete, ChannelLink, UserMove, UserMute, UserKick, UserBan, GroupsManage, GroupsAssign, ServerConfig, ChatServer, ChatChannel, ChatPrivate, ChannelJoinFull, UsersView, BansView, GroupsView, GroupsCreate, GroupsDelete, UserDelete, LogsView, LogsDownload, BackupsManage, ChannelPasswordBypass, All",
        "SoundEvent: MicOff, MicOn, SoundOff, SoundOn, Connected, Disconnected, ChannelEntered, UserJoined, UserLeft, ServerMuted, Moved, PrivateMessage, ServerMessage, ChannelMessage, LinkVoice, OwnLinkVoice, GroupChanged, GroupChangedByMe, TalkKeyDown, TalkKeyUp",
        "TransmitMode: PushToTalk, VoiceActivation",
    ];

    /// <summary>The persisted roots: the server data, the backup manifest and the client settings.</summary>
    static readonly Type[] PersistedRoots = [typeof(ServerData), typeof(BackupManifest), typeof(ClientSettings)];

    [Fact]
    public void Messages_Discriminators_KeepTheirPropertyNames()
    {
        var actual = typeof(Message).GetCustomAttributes<JsonDerivedTypeAttribute>()
            .Select(jsonDerivedTypeAttribute => $"{jsonDerivedTypeAttribute.TypeDiscriminator}: {PropertyNames(jsonDerivedTypeAttribute.DerivedType)}".TrimEnd())
            .Order(StringComparer.Ordinal).ToArray();
        Assert.True(Messages.SequenceEqual(actual), Literal(actual));
    }

    [Fact]
    public void Records_CarriedByMessages_KeepTheirPropertyNames()
    {
        var actual = Reachable(MessageTypes()).Where(candidate => !candidate.IsEnum && !typeof(Message).IsAssignableFrom(candidate))
            .Select(candidate => $"{candidate.Name}: {PropertyNames(candidate)}").Order(StringComparer.Ordinal).ToArray();
        Assert.True(Records.SequenceEqual(actual), Literal(actual));
    }

    [Fact]
    public void ReasonCodes_Values_Unchanged()
    {
        var actual = ReasonCodes.All().Order(StringComparer.Ordinal).ToArray();
        Assert.True(CodeValues.SequenceEqual(actual), Literal(actual));
    }

    [Fact]
    public void Enums_OnWireOrPersisted_KeepTheirMemberNames()
    {
        var actual = Reachable(MessageTypes().Concat(PersistedRoots)).Where(candidate => candidate.IsEnum)
            .Select(candidate => $"{candidate.Name}: {string.Join(", ", Enum.GetNames(candidate))}").Order(StringComparer.Ordinal).ToArray();
        Assert.True(Enums.SequenceEqual(actual), Literal(actual));
    }

    [Fact]
    public void Messages_EveryConcreteSubtype_IsRegistered()
    {
        var concrete = typeof(Message).Assembly.GetTypes().Where(candidate => !candidate.IsAbstract && typeof(Message).IsAssignableFrom(candidate))
            .Select(candidate => candidate.FullName!).Order(StringComparer.Ordinal).ToArray();
        var registered = MessageTypes().Select(candidate => candidate.FullName!).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(concrete, registered);
    }

    static IEnumerable<Type> MessageTypes() => typeof(Message).GetCustomAttributes<JsonDerivedTypeAttribute>().Select(jsonDerivedTypeAttribute => jsonDerivedTypeAttribute.DerivedType);

    static string PropertyNames(Type type) =>
        string.Join(", ", Info(type).Properties.Select(jsonProperty => jsonProperty.Name).Order(StringComparer.Ordinal));

    /// <summary>The contract the wire options give a type (the same one they use when they first serialize it).</summary>
    static JsonTypeInfo Info(Type type)
    {
        ProtocolJson.Options.MakeReadOnly(populateMissingResolver: true);
        return ProtocolJson.Options.GetTypeInfo(type);
    }

    /// <summary>The project types written along with the roots: the types of their serialized properties, lists, dictionaries and nullables unwrapped.</summary>
    static HashSet<Type> Reachable(IEnumerable<Type> roots)
    {
        var seen = new HashSet<Type>();
        var queue = new Queue<Type>(roots);
        while (queue.TryDequeue(out var type))
        {
            if (type.IsGenericType || type.IsArray)
            {
                foreach (var arg in type.IsArray ? [type.GetElementType()!] : type.GetGenericArguments()) queue.Enqueue(arg);
                continue;
            }
            if (type.Namespace?.StartsWith("OVS.", StringComparison.Ordinal) != true || !seen.Add(type) || type.IsEnum) continue;
            foreach (var jsonProperty in Info(type).Properties) queue.Enqueue(jsonProperty.PropertyType);
        }
        return seen;
    }

    /// <summary>The lines as they belong into the literal list, for the failure message.</summary>
    static string Literal(IEnumerable<string> lines) => "Actual:\n" + string.Concat(lines.Select(line => $"        \"{line}\",\n"));
}
