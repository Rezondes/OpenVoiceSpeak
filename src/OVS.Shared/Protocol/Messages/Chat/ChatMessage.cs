namespace OVS.Shared.Protocol.Messages.Chat;

/// <summary>Chat (Package 31). A delivered chat message; the sender gets it back as well, with the server's time.</summary>
public sealed record ChatMessage(
    ChatTarget Target, uint FromSessionId, string FromNickname, Guid? ChannelId, uint? ToSessionId, string Text, DateTimeOffset SentAt) : Message;
