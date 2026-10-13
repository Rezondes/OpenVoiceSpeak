using OVS.Client.Localization;
using OVS.Client.ViewModels;

namespace OVS.Client.Models;

/// <summary>
/// One line in a chat tab: a message (From set), a system notice (Notice set) or a plain marker such as
/// "you entered the channel" (neither).
/// </summary>
/// <param name="Sending">Package 98: an own message on its way to the server, shown before the server's echo.</param>
/// <param name="Time">When it arrived, local time.</param>
/// <param name="Text">The message or notice text.</param>
/// <param name="From">The sender's nickname of a message.</param>
/// <param name="IsOwn">The own user sent it.</param>
/// <param name="Notice">The kind of a system notice.</param>
public sealed record ChatEntry(DateTime Time, string Text, string? From = null, bool IsOwn = false, NoticeKind? Notice = null, OutgoingChatMessageViewModel? Sending = null)
{
    /// <summary>The time as shown.</summary>
    public string TimeText => Formats.ClockTime(Time);
    /// <summary>The entry is someone's message.</summary>
    public bool IsMessage => From is not null;
    /// <summary>An own message: on the right, as a bubble without avatar (Package 64).</summary>
    public bool IsOwnMessage => IsMessage && IsOwn;
    /// <summary>Someone else's message: on the left, with avatar and name.</summary>
    public bool IsOtherMessage => IsMessage && !IsOwn;
    /// <summary>The entry is a system notice.</summary>
    public bool IsNotice => Notice is not null;
    /// <summary>The entry is a plain marker, neither message nor notice.</summary>
    public bool IsMarker => From is null && Notice is null;
    /// <summary>An info notice.</summary>
    public bool IsInfo => Notice is NoticeKind.Info;
    /// <summary>The server's welcome text.</summary>
    public bool IsWelcome => Notice is NoticeKind.Welcome;
    /// <summary>A warning notice.</summary>
    public bool IsWarning => Notice is NoticeKind.Warning;
    /// <summary>An error notice.</summary>
    public bool IsError => Notice is NoticeKind.Error;
}
