namespace OVS.Manager.Servers;

/// <summary>Package M7: why a server could not be created (M8: or started, stopped, restarted), for the text on the page.</summary>
internal enum ServerProblem
{
    /// <summary>The name is empty, too long or has forbidden characters.</summary>
    InvalidName,
    /// <summary>The user limit is outside 1 to 1000.</summary>
    InvalidMaxUsers,
    /// <summary>The time zone is unknown.</summary>
    InvalidTimeZone,
    /// <summary>The port is not within 7000 to 7099.</summary>
    PortOutOfRange,
    /// <summary>Another server has the port.</summary>
    PortTaken,
    /// <summary>All 100 ports are taken.</summary>
    NoFreePort,
    /// <summary>Something else on the host uses the port.</summary>
    PortInUseOnHost,
    /// <summary>Package M8: the server or its container is gone.</summary>
    NotFound,
}
