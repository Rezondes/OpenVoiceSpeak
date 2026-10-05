namespace OVS.Shared.Protocol.Messages.ServerInfo;

/// <summary>Sets the server logo, a square PNG as base64 (see ServerIconFormat); null removes it.</summary>
public sealed record SetServerIcon(string? PngBase64) : Request;
