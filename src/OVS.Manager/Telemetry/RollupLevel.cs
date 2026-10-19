namespace OVS.Manager.Telemetry;

/// <summary>Package M21 (MA7): the period a rolled-up metric value covers.</summary>
internal enum RollupLevel
{
    /// <summary>One minute, kept 7 days by default.</summary>
    Minute,
    /// <summary>One hour, kept 400 days by default.</summary>
    Hour,
}
