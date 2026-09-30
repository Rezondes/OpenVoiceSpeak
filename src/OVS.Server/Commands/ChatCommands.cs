using OVS.Shared.Permissions;
using OVS.Shared.Protocol;

namespace OVS.Server;

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
        var text = TextRules.Text(r.Text, ProtocolInfo.MaxChatLength)?.Trim() ?? "";
        if (text.Length == 0)
        {
            Fail(s, r, Codes.InvalidValue, $"Eine Nachricht hat 1 bis {ProtocolInfo.MaxChatLength} Zeichen, ohne Steuerzeichen.");
            return;
        }

        Session? to = null;
        if (r.Target == ChatTarget.Private)
        {
            to = r.ToSessionId is { } id ? sessions.GetValueOrDefault(id) : null;
            if (to is null)
            {
                Fail(s, r, Codes.NotFound, "Der Empfänger ist nicht online.");
                return;
            }
            if (to == s)
            {
                Fail(s, r, Codes.InvalidValue, "Eine private Nachricht an dich selbst geht nicht.");
                return;
            }
        }

        if (!s.TryChat())
        {
            Fail(s, r, Codes.RateLimited);
            return;
        }
        s.ChatMessages++; // Package 70

        var message = new ChatMessage(r.Target, s.Id, s.Nickname, r.Target == ChatTarget.Channel ? s.ChannelId : null,
            to?.Id, text, time.GetUtcNow());
        switch (r.Target)
        {
            case ChatTarget.Server:
                logs.Server($"Chat von {s.Nickname}: {text}");
                Broadcast(message);
                break;
            case ChatTarget.Channel:
                ChannelLog(s.ChannelId, $"{s.Nickname}: {text}");
                foreach (var member in sessions.Values.Where(x => x.ChannelId == s.ChannelId)) member.Send(message);
                break;
            case ChatTarget.Private:
                logs.Server($"{s.Nickname} schreibt privat an {to!.Nickname}"); // never the content (A29)
                to.Send(message);
                s.Send(message);
                break;
        }
    }
}
