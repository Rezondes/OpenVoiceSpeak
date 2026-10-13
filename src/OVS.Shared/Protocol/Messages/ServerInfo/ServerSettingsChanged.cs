namespace OVS.Shared.Protocol.Messages.ServerInfo;

/// <summary>The server settings changed.</summary>
public sealed record ServerSettingsChanged(ServerSettingsInfo Settings) : Message;
