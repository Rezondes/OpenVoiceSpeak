using OVS.Client.Net;
using OVS.Shared.Logging;
using OVS.Shared.Protocol;

namespace OVS.Client.Logging;

/// <summary>
/// The client's one log: &lt;profile&gt;/logs/client-&lt;start&gt;.log, a new file per client start, kept 30 days. Never contains the server password,
/// the admin token or the identity key. Writing never throws.
/// </summary>
public sealed class ClientLog
{
    public const int KeepDays = 30;
    readonly LogFiles files;

    public ClientLog(string profileDir, TimeProvider time) =>
        files = new LogFiles(Path.Combine(profileDir, "logs"), KeepDays, time, line => System.Diagnostics.Trace.WriteLine(line));

    public void Write(string text) => files.Append("", "client-", $"{files.Stamp()} {text}");

    /// <summary>A server message in words, with names from the mirror. Call before the mirror applies it.</summary>
    /// <returns>Null for messages that are logged elsewhere (errors appear as notices).</returns>
    public static string? Describe(Message message, StateMirror mirror)
    {
        string Channel(Guid id) => $"'{mirror.Channels.GetValueOrDefault(id)?.Name ?? id.ToString()}'";
        string Nick(uint id) => mirror.Users.GetValueOrDefault(id)?.Nickname ?? $"#{id}";

        switch (message)
        {
            case ChannelAdded m:
                return $"Channel '{m.Channel.Name}' angelegt";
            case ChannelUpdated m:
                var before = mirror.Channels.GetValueOrDefault(m.Channel.Id);
                if (before is not null && before.IsMuted != m.Channel.IsMuted)
                    return $"Channel '{m.Channel.Name}' {(m.Channel.IsMuted ? "ist jetzt stumm" : "ist nicht mehr stumm")}";
                return before is null || before.Name == m.Channel.Name
                    ? $"Channel '{m.Channel.Name}' geändert"
                    : $"Channel '{before.Name}' umbenannt in '{m.Channel.Name}'";
            case ChannelRemoved m:
                return $"Channel {Channel(m.ChannelId)} gelöscht";
            case UserJoined m:
                return $"{m.User.Nickname} verbunden, Channel {Channel(m.User.ChannelId)}";
            case UserUpdated m:
                return DescribeUserUpdate(mirror.Users.GetValueOrDefault(m.User.SessionId), m.User, Channel);
            case UserLeft m:
                return $"{Nick(m.SessionId)} getrennt";
            case ChannelsLinked m:
                return $"Channels {Channel(m.A)} und {Channel(m.B)} verlinkt";
            case ChannelsUnlinked m:
                return $"Link zwischen {Channel(m.A)} und {Channel(m.B)} entfernt";
            case GroupsChanged m:
                return $"Gruppen geändert: {string.Join(", ", m.Groups.Select(g => g.Name))}";
            case ServerSettingsChanged m:
                return $"Servereinstellungen geändert: Name '{m.Settings.Name}', {(m.Settings.HasPassword ? "mit" : "ohne")} Passwort";
            case UserList m:
                return $"Nutzerliste erhalten ({m.Users.Count} Einträge)";
            case BanList m:
                return $"Bannliste erhalten ({m.Bans.Count} Einträge)";
            case ChatMessage m:
                return m.Target switch
                {
                    ChatTarget.Server => $"Chat (Allgemein) {m.FromNickname}: {m.Text}",
                    ChatTarget.Channel => $"Chat in {Channel(m.ChannelId ?? Guid.Empty)} {m.FromNickname}: {m.Text}",
                    _ => $"Privat {m.FromNickname} an {Nick(m.ToSessionId ?? 0)}: {m.Text}",
                };
            case ServerIcon m:
                return m.PngBase64 is null ? "Server hat kein Logo" : $"Server-Logo erhalten ({m.PngBase64.Length * 3 / 4 / 1024} KB)";
            case Error { Detail: { Length: > 0 } detail } e:
                return $"Fehler {e.Code} ({e.RequestId}): {detail}"; // A45: the UI shows its own text, the detail only lands here
            case Error:
                return null;
            default:
                return message.GetType().Name;
        }
    }

    static string DescribeUserUpdate(UserInfo? before, UserInfo after, Func<Guid, string> channel)
    {
        if (before is null) return $"{after.Nickname} aktualisiert";
        var changes = new List<string>();
        if (before.ChannelId != after.ChannelId) changes.Add($"wechselt von {channel(before.ChannelId)} nach {channel(after.ChannelId)}");
        if (before.SelfMuted != after.SelfMuted) changes.Add(after.SelfMuted ? "stumm" : "nicht mehr stumm");
        if (before.SelfDeafened != after.SelfDeafened) changes.Add(after.SelfDeafened ? "taub" : "nicht mehr taub");
        if (before.ServerMuted != after.ServerMuted) changes.Add(after.ServerMuted ? "vom Server stummgeschaltet" : "vom Server freigegeben");
        if (!before.GroupIds.SequenceEqual(after.GroupIds) || before.Permissions != after.Permissions) changes.Add($"Rechte jetzt {after.Permissions}");
        return changes.Count == 0 ? $"{after.Nickname} aktualisiert" : $"{after.Nickname} {string.Join(", ", changes)}";
    }

    /// <summary>An own request in words. Passwords and tokens are never part of the text.</summary>
    public static string Describe(Request request, StateMirror mirror)
    {
        string Channel(Guid id) => $"'{mirror.Channels.GetValueOrDefault(id)?.Name ?? id.ToString()}'";
        string Nick(uint id) => mirror.Users.GetValueOrDefault(id)?.Nickname ?? $"#{id}";
        string Group(Guid id) => $"'{mirror.Groups.FirstOrDefault(g => g.Id == id)?.Name ?? id.ToString()}'";
        string Person(string fingerprint) =>
            mirror.Users.Values.FirstOrDefault(u => u.Fingerprint == fingerprint)?.Nickname ?? fingerprint[..Math.Min(12, fingerprint.Length)];
        static string YesNo(bool b) => b ? "ja" : "nein";

        var text = request switch
        {
            JoinChannel r => $"Channel {Channel(r.ChannelId)} betreten",
            CreateChannel r => $"Channel '{r.Name}' anlegen",
            EditChannel r => $"Channel {Channel(r.ChannelId)} bearbeiten: Name '{r.Name}', Reihenfolge {r.Order}, stumm {YesNo(r.IsMuted)}, max. Nutzer {r.MaxUsers}",
            DeleteChannel r => $"Channel {Channel(r.ChannelId)} löschen",
            SetChannelLinks r => $"Links ändern: {r.Add.Count} setzen, {r.Remove.Count} entfernen",
            ReorderChannels r => $"Channels umsortieren: {string.Join(", ", r.ChannelIds.Select(Channel))}",
            ReorderGroups r => $"Gruppen umsortieren: {string.Join(", ", r.GroupIds.Select(id => mirror.Groups.FirstOrDefault(g => g.Id == id)?.Name ?? "?"))}",
            MoveUser r => $"{Nick(r.SessionId)} nach {Channel(r.ChannelId)} verschieben",
            SetSelfState r => $"Eigener Status: stumm {YesNo(r.Muted)}, taub {YesNo(r.Deafened)}",
            LinkChannels r => $"Channels {Channel(r.A)} und {Channel(r.B)} verlinken",
            UnlinkChannels r => $"Link zwischen {Channel(r.A)} und {Channel(r.B)} entfernen",
            CreateGroup r => $"Gruppe '{r.Name}' anlegen ({r.Permissions})",
            UpdateGroup r => $"Gruppe {Group(r.GroupId)} ändern: Name '{r.Name}', Rechte {r.Permissions}",
            DeleteGroup r => $"Gruppe {Group(r.GroupId)} löschen",
            AssignGroup r => $"Gruppe {Group(r.GroupId)} an {Person(r.Fingerprint)} vergeben",
            UnassignGroup r => $"Gruppe {Group(r.GroupId)} von {Person(r.Fingerprint)} entfernen",
            ListUsers => "Nutzerliste anfordern",
            ListBans => "Bannliste anfordern",
            RedeemAdminToken => "Admin-Token einlösen",
            SetServerIcon r => r.PngBase64 is null ? "Server-Logo entfernen" : "Server-Logo setzen",
            GetServerIcon => "Server-Logo anfordern",
            SendChat r => r.Target switch
            {
                ChatTarget.Server => "Chat an alle",
                ChatTarget.Channel => "Chat im Channel",
                _ => $"Privatnachricht an {Nick(r.ToSessionId ?? 0)}",
            },
            UpdateServerSettings r => $"Servereinstellungen ändern: Name '{r.Name}'" + r.Password switch
            {
                null => "",
                "" => ", Passwort entfernen",
                _ => ", Passwort setzen",
            },
            Kick r => $"{Nick(r.SessionId)} kicken: {r.Reason}",
            Ban r => $"{Nick(r.SessionId)} bannen ({(r.DurationMinutes is { } m ? $"{m} Minuten" : "dauerhaft")}{(r.IncludeIp ? ", mit IP" : "")}): {r.Reason}",
            Unban r => $"Bann {r.BanId} aufheben",
            SetServerMute r => $"{Nick(r.SessionId)} serverseitig {(r.Muted ? "stummschalten" : "freigeben")}",
            _ => request.GetType().Name,
        };
        return $"Anfrage {request.RequestId}: {text}";
    }
}
