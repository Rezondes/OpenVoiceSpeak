namespace OVS.Shared.Protocol.Messages.ServerInfo;

public sealed record ServerSettingsChanged(ServerSettingsInfo Settings) : Message;
