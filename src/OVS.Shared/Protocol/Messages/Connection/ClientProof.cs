namespace OVS.Shared.Protocol.Messages.Connection;

/// <summary>The client's signature over the challenge, proving it holds the key of its public key.</summary>
public sealed record ClientProof(string Signature) : Message;
