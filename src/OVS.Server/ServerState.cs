using System.Net;
using System.Security.Cryptography;
using System.Text;
using OVS.Shared.Protocol;

namespace OVS.Server;

/// <summary>Who is connected. Every change runs under one lock.</summary>
public sealed class ServerState
{
    const int MaxConnectionsPerIp = 5;

    readonly object gate = new();
    readonly ServerConfig config;
    readonly Action<string> log;
    readonly Dictionary<uint, Session> sessions = [];
    readonly Dictionary<IPAddress, int> connectionsPerIp = [];
    uint lastSessionId;

    public ServerState(ServerConfig config, TimeProvider time, Action<string> log)
    {
        this.config = config;
        this.log = log;
    }

    public int SessionCount
    {
        get { lock (gate) return sessions.Count; }
    }

    /// <summary>Counts the connection. Always pair with ReleaseConnection.</summary>
    public bool TryAddConnection(IPAddress ip)
    {
        lock (gate)
        {
            int count = connectionsPerIp.GetValueOrDefault(ip) + 1;
            connectionsPerIp[ip] = count;
            return count <= MaxConnectionsPerIp;
        }
    }

    public void ReleaseConnection(IPAddress ip)
    {
        lock (gate)
        {
            int count = connectionsPerIp.GetValueOrDefault(ip) - 1;
            if (count <= 0) connectionsPerIp.Remove(ip);
            else connectionsPerIp[ip] = count;
        }
    }

    public (Session? Session, Rejected? Rejection) Admit(string fingerprint, string nickname, IPAddress ip, string? password)
    {
        lock (gate)
        {
            if (!PasswordMatches(password)) return (null, new Rejected(Codes.WrongPassword));

            var replaced = sessions.Values.FirstOrDefault(s => s.Fingerprint == fingerprint);
            if (sessions.Count - (replaced is null ? 0 : 1) >= config.MaxUsers)
                return (null, new Rejected(Codes.ServerFull));
            if (sessions.Values.Any(s => s != replaced && string.Equals(s.Nickname, nickname, StringComparison.OrdinalIgnoreCase)))
                return (null, new Rejected(Codes.NicknameTaken));

            if (replaced is not null) RemoveLocked(replaced, new Disconnected(Codes.ReplacedByNewConnection));

            var session = new Session(++lastSessionId, fingerprint, nickname, ip, RandomNumberGenerator.GetBytes(32));
            sessions.Add(session.Id, session);
            var settings = new ServerSettingsInfo(config.ServerName, "", config.Password.Length > 0);
            session.Send(new Welcome(session.Id, Convert.ToBase64String(session.VoiceKey),
                new ServerSnapshot(settings, Guid.Empty, [], [], [], [])));
            log($"{nickname} verbunden ({fingerprint[..12]}, {ip})");
            return (session, null);
        }
    }

    bool PasswordMatches(string? password) =>
        config.Password.Length == 0 || CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(password ?? "")),
            SHA256.HashData(Encoding.UTF8.GetBytes(config.Password)));

    public void Remove(Session session)
    {
        lock (gate) RemoveLocked(session, null);
    }

    public void CloseAll(Message final)
    {
        lock (gate)
        {
            foreach (var s in sessions.Values) s.Close(final);
            sessions.Clear();
        }
    }

    void RemoveLocked(Session session, Message? final)
    {
        session.Close(final);
        if (!sessions.TryGetValue(session.Id, out var current) || current != session) return;
        sessions.Remove(session.Id);
        log($"{session.Nickname} getrennt");
    }

    public void Handle(Session session, Message message)
    {
        if (message is Request r) session.Send(new Error(r.RequestId, Codes.UnknownRequest));
    }

    /// <summary>Trimmed name of 1..max chars without control characters, else null.</summary>
    public static string? ValidName(string? name, int max)
    {
        var n = name?.Trim();
        return n is { Length: > 0 } && n.Length <= max && !n.Any(char.IsControl) ? n : null;
    }
}
