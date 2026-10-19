namespace OVS.Manager.Telemetry;

/// <summary>Package M21 (AC4): whether the last check had to delete raw data to stay under the size limit; the overview warns then.</summary>
internal sealed class TelemetryLimit
{
    /// <summary>The last check deleted raw data for the size.</summary>
    public bool Reached { get; set; }
}
