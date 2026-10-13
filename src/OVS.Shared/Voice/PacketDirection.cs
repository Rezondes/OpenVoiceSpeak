namespace OVS.Shared.Voice;

/// <summary>Which way a voice packet travels; part of its nonce, so a packet cannot be sent back.</summary>
#pragma warning disable CA1028 // one byte on the wire
public enum PacketDirection : byte
#pragma warning restore CA1028
{
    /// <summary>From the client to the server.</summary>
    ClientToServer = 0,
    /// <summary>From the server to the client.</summary>
    ServerToClient = 1,
}
