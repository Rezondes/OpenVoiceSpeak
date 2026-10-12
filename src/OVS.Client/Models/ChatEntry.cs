using OVS.Client.Localization;
using OVS.Client.ViewModels;

namespace OVS.Client.Models;

/// <summary>
/// One line in a chat tab: a message (From set), a system notice (Notice set) or a plain marker such as
/// "you entered the channel" (neither).
/// </summary>
/// <param name="Sending">Package 98: an own message on its way to the server, shown before the server's echo.</param>
public sealed record ChatEntry(DateTime Time, string Text, string? From = null, bool IsOwn = false, NoticeKind? Notice = null, OutgoingChatMessageViewModel? Sending = null)
{
    public string TimeText => Formats.ClockTime(Time);
    public bool IsMessage => From is not null;
    public bool IsOwnMessage => IsMessage && IsOwn;     // Package 64: right, as a bubble without avatar
    public bool IsOtherMessage => IsMessage && !IsOwn;  // left, with avatar and name
    public bool IsNotice => Notice is not null;
    public bool IsMarker => From is null && Notice is null;
    public bool IsInfo => Notice is NoticeKind.Info;
    public bool IsWelcome => Notice is NoticeKind.Welcome;
    public bool IsWarning => Notice is NoticeKind.Warning;
    public bool IsError => Notice is NoticeKind.Error;
}
