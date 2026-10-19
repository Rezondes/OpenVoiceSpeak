using System.Globalization;
using OVS.Manager.Control;
using OVS.Manager.Servers;

namespace OVS.Manager.Localization;

/// <summary>Package M8: the texts of a server's problem, status and uptime, shared by the server pages.</summary>
internal static class ServerTexts
{
    /// <summary>Package M7: why a server could not be created, started, stopped or restarted.</summary>
    public static string Problem(ServerProblem problem) => problem switch
    {
        ServerProblem.InvalidName => Strings.Servers_InvalidName,
        ServerProblem.InvalidMaxUsers => TextFormat.Of(Strings.Servers_InvalidMaxUsers, ServerFactory.MaxUsersLimit),
        ServerProblem.InvalidTimeZone => Strings.Servers_InvalidTimeZone,
        ServerProblem.PortOutOfRange => TextFormat.Of(Strings.Servers_PortOutOfRange, PortPool.First, PortPool.First + PortPool.Count - 1),
        ServerProblem.PortTaken => Strings.Servers_PortTaken,
        ServerProblem.NoFreePort => Strings.Servers_AllPortsTaken,
        ServerProblem.PortInUseOnHost => Strings.Servers_PortInUseOnHost,
        ServerProblem.NameMismatch => Strings.Servers_NameMismatch,
        _ => Strings.Servers_NotFound,
    };

    /// <summary>AC1, AC3: the status, with the last exit code when it crashed or loops.</summary>
    public static string Status(ServerSnapshot server)
    {
        var text = server.Status switch
        {
            ServerStatus.Running => Strings.Status_Running,
            ServerStatus.Starting => Strings.Status_Starting,
            ServerStatus.Stopped => Strings.Status_Stopped,
            ServerStatus.Crashed => Strings.Status_Crashed,
            ServerStatus.RestartLoop => Strings.Status_RestartLoop,
            _ => Strings.Status_NotReachable,
        };
        return server.ExitCode is { } code ? TextFormat.Of(Strings.Status_WithExitCode, text, code) : text;
    }

    /// <summary>Package M11 (AC4): the users online but the service session, "–" without a session.</summary>
    public static string UsersOnline(ServerLink? link) =>
        link?.Read(mirror => mirror.Users.Keys.Count(session => session != mirror.SelfId), -1) is >= 0 and var count
            ? count.ToString(CultureInfo.CurrentCulture)
            : Strings.Link_Offline;

    /// <summary>Package M11 (AC1 to AC3): the state of the service session.</summary>
    public static string Link(ServerLink? link) => link?.Status switch
    {
        null => Strings.Link_Offline,
        ServerLinkStatus.Connected => Strings.Link_Connected,
        ServerLinkStatus.Outdated when link.OutdatedProtocol is { } protocol => TextFormat.Of(Strings.Link_OutdatedProtocol, protocol),
        ServerLinkStatus.Outdated => Strings.Link_Outdated,
        ServerLinkStatus.CertificateChanged => Strings.Link_CertificateChanged,
        _ => Strings.Link_Connecting,
    };

    /// <summary>AC1: how long the container runs, empty when it does not.</summary>
    public static string Uptime(ServerSnapshot server, DateTimeOffset now) =>
        server.StartedAtUtc is { } started && now - started is { Ticks: >= 0 } span
            ? TextFormat.Of(Strings.Servers_Uptime, (int)span.TotalDays, span.Hours, span.Minutes)
            : "";
}
