namespace OVS.Shared.Voice;

/// <summary>Which way a voice packet travels; part of its nonce, so a packet cannot be sent back.</summary>
public enum PacketDirection : byte
{
    /// <summary>From the client to the server.</summary>
    ClientToServer = 0,
    /// <summary>From the server to the client.</summary>
    ServerToClient = 1,
}
