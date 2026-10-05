using OVS.Shared.Protocol.Messages.Moderation;

namespace OVS.Shared.Protocol.Messages.Users;

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
