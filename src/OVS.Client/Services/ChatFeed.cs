using OVS.Client.Audio.Sounds;
using OVS.Shared.Protocol.Messages.Chat;

namespace OVS.Client.Services;

/// <summary>
/// The chat of the connected server (Package 31): the messages as they come, with a tone for each one from someone else
/// (Package 56), the last 200 kept for the debug view, and the server's refusals of the own chat requests for the composer.
/// </summary>
internal sealed class ChatFeed
{
    /// <summary>Chat requests get their own id prefix, so their errors can go to the composer instead of the notices.</summary>
    internal const string RequestPrefix = "c";

    /// <summary>How many messages <see cref="Recent"/> keeps.</summary>
    const int KeptCount = 200;

    readonly Queue<ChatMessage> recent = new();
    readonly Action<SoundEvent> requestSound;

    /// <param name="requestSound">Plays a tone (the server view model's tone request).</param>
    internal ChatFeed(Action<SoundEvent> requestSound)
    {
        this.requestSound = requestSound;
    }

    /// <summary>The last messages, oldest first, at most 200.</summary>
    internal IReadOnlyCollection<ChatMessage> Recent => recent;

    /// <summary>A chat message arrived (after its tone and after it was kept).</summary>
    internal event Action<ChatMessage>? Received;

    /// <summary>The server refused an own chat request; the text says why.</summary>
    internal event Action<string>? Refused;

    /// <summary>A chat message arrived: a tone of its target unless it is the own one, kept, then told.</summary>
    internal void Receive(ChatMessage chat, uint selfId)
    {
        if (chat.FromSessionId != selfId) // Package 56: a sound per chat, never for the own messages
            requestSound(chat.Target switch
            {
                ChatTarget.Private => SoundEvent.PrivateMessage,
                ChatTarget.Channel => SoundEvent.ChannelMessage,
                _ => SoundEvent.ServerMessage,
            });
        recent.Enqueue(chat);
        while (recent.Count > KeptCount) recent.Dequeue();
        Received?.Invoke(chat);
    }

    /// <summary>
    /// Tells the composer that the server refused the request <paramref name="requestId"/>, when it was a chat request
    /// and someone listens; false otherwise, so the caller shows the text as a notice.
    /// </summary>
    internal bool TryRefuse(string? requestId, string text)
    {
        if (requestId?.StartsWith(RequestPrefix, StringComparison.Ordinal) != true || Refused is not { } refused) return false;
        refused(text);
        return true;
    }
}
