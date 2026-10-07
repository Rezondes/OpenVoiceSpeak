using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages.Chat;

namespace OVS.Server.State;

/// <summary>
/// Text chat (Package 31): server-wide, in the own channel, or private between two online users.
/// Nothing is stored (A27). Server-muted users may still write (A28).
/// </summary>
public sealed partial class ServerState
{
    void OnSendChat(Session s, SendChat r)
    {
        var right = r.Target switch
        {
            ChatTarget.Server => Permission.ChatServer,
            ChatTarget.Channel => Permission.ChatChannel,
            _ => Permission.ChatPrivate,
        };
        if (!Require(s, r, right)) return;

        // Package 83: line breaks are fine, other control and bidi characters are not
        var text = TextRules.NormalizeText(r.Text, ProtocolLimits.MaxChatLength)?.Trim() ?? "";
        if (text.Length == 0)
        {
            Fail(s, r, ReasonCodes.InvalidValue, $"Eine Nachricht hat 1 bis {ProtocolLimits.MaxChatLength} Zeichen, ohne Steuerzeichen.");
            return;
        }

        Session? to = null;
        if (r.Target == ChatTarget.Private)
        {
            to = r.ToSessionId is { } id ? sessions.GetValueOrDefault(id) : null;
            if (to is null)
            {
                Fail(s, r, ReasonCodes.NotFound, "Der Empfänger ist nicht online.");
                return;
            }
            if (to == s)
            {
                Fail(s, r, ReasonCodes.InvalidValue, "Eine private Nachricht an dich selbst geht nicht.");
                return;
            }
        }

        if (!s.Throttle.TryChat())
        {
            Fail(s, r, ReasonCodes.RateLimited);
            return;
        }
        s.ChatMessages++; // Package 70

        var message = new ChatMessage(r.Target, s.Id, s.Nickname, r.Target == ChatTarget.Channel ? s.ChannelId : null,
            to?.Id, text, time.GetUtcNow());
        switch (r.Target)
        {
            case ChatTarget.Server:
                logs.WriteServerLine($"Chat von {s.Nickname}: {text}");
                Broadcast(message);
                break;
            case ChatTarget.Channel:
                ChannelLog(s.ChannelId, $"{s.Nickname}: {text}");
                foreach (var member in sessions.Values.Where(x => x.ChannelId == s.ChannelId)) member.Outbox.Send(message);
                break;
            case ChatTarget.Private:
                logs.WriteServerLine($"{s.Nickname} schreibt privat an {to!.Nickname}"); // never the content (A29)
                to.Outbox.Send(message);
                s.Outbox.Send(message);
                break;
        }
    }
}
