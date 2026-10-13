using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.LogAccess;
using OVS.Shared.Protocol.Messages.Moderation;
using OVS.Shared.Protocol.Messages.ServerBackups;
using OVS.Shared.Protocol.Messages.Users;

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

    /// <summary>
    /// Hands a routed answer (a list, a backup or log transfer answer) to the route of its request and returns true; any
    /// other message, the server's error included (its sender delivers it after the notice), returns false.
    /// </summary>
    internal bool DeliverAnswer(Message answer)
    {
        if (!TryReadRoutedRequestId(answer, out var requestId)) return false;
        Deliver(requestId, answer);
        return true;
    }

    /// <summary>The request id of a routed answer type (Packages 73 to 75, 81 and 82); false for every other message.</summary>
    static bool TryReadRoutedRequestId(Message answer, out string? requestId)
    {
        requestId = answer switch
        {
            UserList list => list.RequestId,
            BanList list => list.RequestId,
            BackupList list => list.RequestId,
            BackupChunk chunk => chunk.RequestId,
            UploadBackupAck ack => ack.RequestId,
            BackupUploaded uploaded => uploaded.RequestId,
            LogList list => list.RequestId,
            LogPage page => page.RequestId,
            LogSearchResult result => result.RequestId,
            LogDownloadReady ready => ready.RequestId,
            LogChunk chunk => chunk.RequestId,
            _ => null,
        };
        return answer is UserList or BanList or BackupList or BackupChunk or UploadBackupAck or BackupUploaded
            or LogList or LogPage or LogSearchResult or LogDownloadReady or LogChunk;
    }

    /// <summary>Removes every route to handler (delegates are equal by target and method): no further answer reaches it.</summary>
    internal void RemoveAll(Action<Message> handler)
    {
        foreach (var id in handlers.Where(pair => pair.Value == handler).Select(pair => pair.Key).ToList()) handlers.Remove(id);
    }

    /// <summary>The handler the answers to the request go to, null when they go nowhere.</summary>
    internal Action<Message>? Find(string requestId) => handlers.GetValueOrDefault(requestId);
}
