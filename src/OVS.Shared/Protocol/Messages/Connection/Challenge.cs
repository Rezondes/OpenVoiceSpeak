namespace OVS.Shared.Protocol.Messages.Connection;

/// <summary>The server's random nonce the client signs together with the server certificate's fingerprint.</summary>
public sealed record Challenge(string Nonce) : Message;
