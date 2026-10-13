using OVS.Server.Logging;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages.Chat;

namespace OVS.Server.State;

/// <summary>
/// Text chat (Package 31): server-wide, in the own channel, or private between two online users.
/// Nothing is stored (A27). Server-muted users may still write (A28).
/// </summary>
internal sealed partial class ServerState
{
    void OnSendChat(Session session, SendChat request)
    {
        var right = request.Target switch
        {
            ChatTarget.Server => Permission.ChatServer,
            ChatTarget.Channel => Permission.ChatChannel,
            _ => Permission.ChatPrivate,
        };
        if (!Require(session, request, right)) return;

        // Package 83: line breaks are fine, other control and bidi characters are not
        var text = TextRules.NormalizeText(request.Text, ProtocolLimits.MaxChatLength)?.Trim() ?? "";
        if (text.Length == 0)
        {
            Fail(session, request, ReasonCodes.InvalidValue, $"A message has 1 to {ProtocolLimits.MaxChatLength} characters and no control characters.");
            return;
        }

        Session? to = null;
        if (request.Target == ChatTarget.Private)
        {
            to = request.ToSessionId is { } id ? sessions.GetValueOrDefault(id) : null;
            if (to is null)
            {
                Fail(session, request, ReasonCodes.NotFound, "The recipient is not online.");
                return;
            }
            if (to == session)
            {
                Fail(session, request, ReasonCodes.InvalidValue, "A private message to yourself is not possible.");
                return;
            }
        }

        if (!session.Throttle.TryChat())
        {
            Fail(session, request, ReasonCodes.RateLimited);
            return;
        }
        session.ChatMessages++; // Package 70

        var message = new ChatMessage(request.Target, session.Id, session.Nickname, request.Target == ChatTarget.Channel ? session.ChannelId : null,
            to?.Id, text, time.GetUtcNow());
        switch (request.Target)
        {
            case ChatTarget.Server:
                logs.Server.ServerChat(session.Nickname, text);
                Broadcast(message);
                break;
            case ChatTarget.Channel:
                ChannelLogger(session.ChannelId)?.Chat(session.Nickname, text);
                foreach (var member in sessions.Values.Where(otherSession => otherSession.ChannelId == session.ChannelId)) member.Outbox.Send(message);
                break;
            case ChatTarget.Private:
                logs.Server.PrivateChat(session.Nickname, to!.Nickname); // never the content (A29)
                to.Outbox.Send(message);
                session.Outbox.Send(message);
                break;
        }
    }
}
