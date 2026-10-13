using OVS.Shared.Protocol.Messages;

namespace OVS.Server.State;

internal sealed partial class ServerState
{
    void BroadcastExcept(Session except, Message message)
    {
        var frame = SessionOutbox.Encode(message);
        foreach (var session in sessions.Values.Where(otherSession => otherSession != except)) session.Outbox.SendFrame(frame, message.GetType().Name);
    }

    void Broadcast(Message message)
    {
        if (sessions.Count == 0) return;
        var frame = SessionOutbox.Encode(message); // Package 86: once for everyone
        foreach (var session in sessions.Values) session.Outbox.SendFrame(frame, message.GetType().Name);
    }
}
