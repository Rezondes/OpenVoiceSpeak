namespace OVS.Shared.Protocol;

/// <summary>The values server and client start from when nothing else is set; each limited default lies inside its <see cref="ProtocolLimits"/> range.</summary>
public static class ProtocolDefaults
{
    public const int Port = 7000;
    // Package 132 (A141): a channel's voice bitrate in kbps
    public const int Bitrate = 64;
    // Package 69: the defaults of the server limits (ServerLimits)
    public const int MaxUsers = 50;
    public const int LogDays = 30; // 0 keeps every log file
}
