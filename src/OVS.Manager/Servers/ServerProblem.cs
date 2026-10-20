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
    /// <summary>Package M9: the name typed to delete it is not exactly the server's.</summary>
    NameMismatch,
    /// <summary>Package M25: a CPU or memory limit is out of its range.</summary>
    InvalidLimits,
    /// <summary>Package M29: a foreign container has nothing mounted at /data to keep.</summary>
    NoDataVolume,
    /// <summary>The server image of the version is neither on the host nor in the registry.</summary>
    ImageMissing,
    /// <summary>A foreign server whose image predates the manager (no protocol label) or names no version.</summary>
    TooOldForManager,
}
