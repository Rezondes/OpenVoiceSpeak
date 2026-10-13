namespace OVS.Shared.Protocol.Messages.Connection;

/// <summary>The handshake succeeded: the session id, the voice key for UDP and the server's state.</summary>
public sealed record Welcome(uint SessionId, string VoiceKey, ServerSnapshot Snapshot) : Message;
