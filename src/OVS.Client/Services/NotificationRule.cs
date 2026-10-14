using System.Text.RegularExpressions;
using OVS.Client.Localization;
using OVS.Shared.Protocol.Messages.Chat;

namespace OVS.Client.Services;

/// <summary>
/// Package 162: decides which chat message or join becomes a desktop notification. Private messages and mentions of
/// the own nickname after "@" (as a whole word, any case) notify, and with its switch a user joining the own channel;
/// never own messages, never while the window is in front. Within 5 s a sender's further notifications are counted
/// into the one shown, which the notifier replaces.
/// </summary>
/// <param name="time">The clock of the 5 s.</param>
internal sealed class NotificationRule(TimeProvider time)
{
    /// <summary>How many characters of a message the notification shows.</summary>
    public const int PreviewLength = 100;

    static readonly TimeSpan MergeWithin = TimeSpan.FromSeconds(5);

    readonly Dictionary<string, (DateTimeOffset At, int Count)> lastBySender = [];

    /// <summary>The notification for a chat message, null when it gives none.</summary>
    /// <param name="message">The message as the server delivered it.</param>
    /// <param name="partner">The sender's fingerprint, for the tab of a private message.</param>
    /// <param name="state">The switches, the own user and whether the window is in front.</param>
    public DesktopNotice? ForMessage(ChatMessage message, string? partner, NotificationState state)
    {
        if (state.WindowActive || message.FromSessionId == state.SelfId) return null;
        bool isPrivate = message.Target == ChatTarget.Private;
        if (isPrivate ? !state.Settings.NotifyPrivate : !(state.Settings.NotifyMention && Mentions(message.Text, state.OwnNickname))) return null;
        var title = isPrivate ? message.FromNickname : TextFormat.Of(Strings.Notify_MentionTitle, message.FromNickname);
        return Merged(new DesktopNotice(message.FromNickname, title, Preview(message.Text), message.Target, isPrivate ? partner : null));
    }

    /// <summary>The notification for a user joining the own channel, null when it gives none.</summary>
    /// <param name="nickname">Who joined.</param>
    /// <param name="state">The switches and whether the window is in front.</param>
    public DesktopNotice? ForJoin(string nickname, NotificationState state) =>
        state.WindowActive || !state.Settings.NotifyJoin ? null
        : Merged(new DesktopNotice(nickname, Strings.Notify_JoinTitle, TextFormat.Of(Strings.Notify_Joined, nickname), ChatTarget.Channel));

    /// <summary>"@" and the nickname as a whole word, any case.</summary>
    static bool Mentions(string text, string nickname) =>
        nickname.Length > 0 && Regex.IsMatch(text, $@"(?<![\w@])@{Regex.Escape(nickname)}(?!\w)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    static string Preview(string text) => text.Length <= PreviewLength ? text : text[..PreviewLength] + "…";

    /// <summary>A sender's second notification within 5 s of the first is counted into it instead.</summary>
    DesktopNotice Merged(DesktopNotice notice)
    {
        var now = time.GetUtcNow();
        if (lastBySender.TryGetValue(notice.Sender, out var last) && now - last.At < MergeWithin)
        {
            lastBySender[notice.Sender] = (last.At, last.Count + 1);
            return notice with { Text = TextFormat.Of(Strings.Notify_More, last.Count + 1, notice.Sender) };
        }
        lastBySender[notice.Sender] = (now, 1);
        return notice;
    }
}
