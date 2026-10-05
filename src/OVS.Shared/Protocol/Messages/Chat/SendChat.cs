namespace OVS.Shared.Protocol.Messages.Chat;

/// <summary>
/// Chat (Package 31).
/// Server: everyone. Channel: the sender's current channel (not linked ones). Private: exactly one online user.
/// </summary>
public sealed record SendChat(ChatTarget Target, uint? ToSessionId, string Text) : Request;
