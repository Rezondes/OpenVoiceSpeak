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

    /// <summary>AC1: how long the container runs, empty when it does not.</summary>
    public static string Uptime(ServerSnapshot server, DateTimeOffset now) =>
        server.StartedAtUtc is { } started && now - started is { Ticks: >= 0 } span
            ? TextFormat.Of(Strings.Servers_Uptime, (int)span.TotalDays, span.Hours, span.Minutes)
            : "";
}
