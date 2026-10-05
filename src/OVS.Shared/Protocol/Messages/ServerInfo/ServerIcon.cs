namespace OVS.Shared.Protocol.Messages.ServerInfo;

public sealed record ServerIcon(string? RequestId, string? Hash, string? PngBase64) : Message;
