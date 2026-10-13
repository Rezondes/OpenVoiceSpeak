using OVS.Client.Localization;

namespace OVS.Client.Models;

/// <summary>A system notice, shown in the chat tab "Allgemein". ToString keeps the "time  text" form for the debug API.</summary>
internal sealed record Notice(DateTime Time, string Text, NoticeKind Kind)
{
    /// <summary>The time as shown.</summary>
    public string TimeText => Formats.ClockTime(Time);
    /// <summary>An info notice.</summary>
    public bool IsInfo => Kind == NoticeKind.Info;
    /// <summary>The server's welcome text.</summary>
    public bool IsWelcome => Kind == NoticeKind.Welcome;
    /// <summary>A warning notice.</summary>
    public bool IsWarning => Kind == NoticeKind.Warning;
    /// <summary>An error notice.</summary>
    public bool IsError => Kind == NoticeKind.Error;
    /// <summary>Time and text, as copied.</summary>
    public override string ToString() => $"{Time:HH:mm:ss}  {Text}";
}
