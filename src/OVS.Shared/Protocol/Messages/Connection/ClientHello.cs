namespace OVS.Shared.Protocol.Messages.Connection;

public sealed record ClientHello(int ProtocolVersion, string Nickname, string PublicKey, string? Password) : Message;
