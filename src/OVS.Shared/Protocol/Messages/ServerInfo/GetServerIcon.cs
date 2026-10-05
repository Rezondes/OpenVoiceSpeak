namespace OVS.Shared.Protocol.Messages.ServerInfo;

/// <summary>Asks for the logo; clients only do this when the hash differs from their cache.</summary>
public sealed record GetServerIcon : Request;
