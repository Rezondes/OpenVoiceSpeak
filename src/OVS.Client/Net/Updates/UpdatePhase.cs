namespace OVS.Client.Net.Updates;

/// <summary>Where an update stands.</summary>
public enum UpdatePhase
{
    /// <summary>The new exe is loading.</summary>
    Downloading,
    /// <summary>Its hash is checked.</summary>
    Verifying,
    /// <summary>The new version starts.</summary>
    Starting,
}
