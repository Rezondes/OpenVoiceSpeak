namespace OVS.Manager.Servers;

/// <summary>Package M7: why a port cannot be given to a new server.</summary>
internal enum PortProblem
{
    /// <summary>The port can be taken.</summary>
    None,
    /// <summary>Not within 7000 to 7099.</summary>
    OutOfRange,
    /// <summary>Another server of the manager has it.</summary>
    Taken,
}
