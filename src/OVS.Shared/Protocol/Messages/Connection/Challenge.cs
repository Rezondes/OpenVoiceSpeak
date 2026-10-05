namespace OVS.Shared.Protocol.Messages.Connection;

public sealed record Challenge(string Nonce) : Message;
