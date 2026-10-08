using OVS.Shared.Protocol.Messages;

namespace OVS.Client.Services;

/// <summary>
/// Who gets the server's answers to a request, by its request id. A route stays until its owner removes it, since one
/// request can be answered more than once (a list in pages, an uploaded backup and then the new list).
/// </summary>
internal sealed class AnswerRoutes
{
    readonly Dictionary<string, Action<Message>> handlers = [];

    /// <summary>The answers to the request with that id go to handler from now on.</summary>
    internal void Add(string requestId, Action<Message> handler) => handlers[requestId] = handler;

    /// <summary>Hands an answer to the handler of its request; one nobody waits for any more (its owner closed) is dropped.</summary>
    internal void Deliver(string? requestId, Message answer)
    {
        if (requestId is not null && handlers.TryGetValue(requestId, out var handler)) handler(answer);
    }

    /// <summary>Removes every route to handler (delegates are equal by target and method): no further answer reaches it.</summary>
    internal void RemoveAll(Action<Message> handler)
    {
        foreach (var id in handlers.Where(h => h.Value == handler).Select(h => h.Key).ToList()) handlers.Remove(id);
    }

    /// <summary>The handler the answers to the request go to, null when they go nowhere.</summary>
    internal Action<Message>? Find(string requestId) => handlers.GetValueOrDefault(requestId);
}
