using OVS.Shared.Protocol.Messages;

namespace OVS.Shared.Net;

/// <summary>Package 194: one message the server sent after the Welcome, raised by <see cref="ControlConnection.MessageReceived"/>.</summary>
/// <param name="message">The message.</param>
public sealed class MessageReceivedEventArgs(Message message) : EventArgs
{
    /// <summary>The message.</summary>
    public Message Message { get; } = message;
}
