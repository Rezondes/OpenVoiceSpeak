namespace OVS.Shared.Protocol.Messages.Connection;

public sealed record Welcome(uint SessionId, string VoiceKey, ServerSnapshot Snapshot) : Message;
