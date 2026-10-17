namespace OVS.Shared.Protocol;

/// <summary>The values server and client start from when nothing else is set; each limited default lies inside its <see cref="ProtocolLimits"/> range.</summary>
public static class ProtocolDefaults
{
    /// <summary>The server's TCP and UDP port.</summary>
    public const int Port = 7000;
    // Package 132 (A141): a channel's voice bitrate in kbps; Package 183 (A194): 128 instead of 64
    /// <summary>A new channel's voice bitrate in kbps (Package 132, A141; Package 183, A194).</summary>
    public const int Bitrate = 128;
    // Package 69: the defaults of the server limits (ServerLimits)
    /// <summary>The default of the server's user limit (Package 69).</summary>
    public const int MaxUsers = 50;
    /// <summary>The default of the log retention in days (Package 69); 0 keeps every log file.</summary>
    public const int LogDays = 30;
}
