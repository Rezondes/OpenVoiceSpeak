namespace OVS.Manager.Servers;

/// <summary>Package M8 (AC1): what the overview says about a server.</summary>
internal enum ServerStatus
{
    /// <summary>The container runs and the server finished a TLS handshake (AC4).</summary>
    Running,
    /// <summary>The container was just started or Docker is restarting it.</summary>
    Starting,
    /// <summary>Stopped by hand, or created and never started.</summary>
    Stopped,
    /// <summary>The container ended with an exit code other than 0.</summary>
    Crashed,
    /// <summary>More than three restarts within ten minutes (AC3).</summary>
    RestartLoop,
    /// <summary>The container runs but the server does not answer, or there is no container.</summary>
    NotReachable,
}
