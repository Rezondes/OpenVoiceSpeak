namespace OVS.Shared.Voice;

/// <summary>The kind of a UDP voice packet.</summary>
#pragma warning disable CA1028 // one byte on the wire
public enum PacketType : byte
#pragma warning restore CA1028
{
    /// <summary>Binds the client's UDP address to its session.</summary>
    Hello = 0,
    /// <summary>One voice frame.</summary>
    Voice = 1,
    /// <summary>Keeps the UDP path open and measures round trip and loss.</summary>
    Ping = 2,
}
