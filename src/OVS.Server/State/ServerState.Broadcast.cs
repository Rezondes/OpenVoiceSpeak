using OVS.Shared.Protocol.Messages;

namespace OVS.Server.State;

public sealed partial class ServerState
{
    void BroadcastExcept(Session except, Message message)
    {
        var frame = SessionOutbox.Encode(message);
        foreach (var s in sessions.Values.Where(s => s != except)) s.Outbox.SendFrame(frame);
    }

    void Broadcast(Message message)
    {
        if (sessions.Count == 0) return;
        var frame = SessionOutbox.Encode(message); // Package 86: once for everyone
        foreach (var s in sessions.Values) s.Outbox.SendFrame(frame);
    }
}
