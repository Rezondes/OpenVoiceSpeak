namespace OVS.Shared.Protocol.Messages.Connection;

/// <summary>The first message of the handshake: who connects and with which protocol version.</summary>
public sealed record ClientHello(int ProtocolVersion, string Nickname, string PublicKey, string? Password) : Message;
