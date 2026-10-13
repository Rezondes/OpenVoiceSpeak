namespace OVS.Shared.Protocol.Messages.ServerInfo;

/// <summary>The server logo: Hash and PngBase64 are null when the server has none.</summary>
public sealed record ServerIcon(string? RequestId, string? Hash, string? PngBase64) : Message;
