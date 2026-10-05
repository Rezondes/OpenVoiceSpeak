using OVS.Client.Localization;

namespace OVS.Client.Models;

/// <summary>A system notice, shown in the chat tab "Allgemein". ToString keeps the "time  text" form for the debug API.</summary>
public sealed record Notice(DateTime Time, string Text, NoticeKind Kind)
{
    public string TimeText => Formats.ClockTime(Time);
    public bool IsInfo => Kind == NoticeKind.Info;
    public bool IsWelcome => Kind == NoticeKind.Welcome;
    public bool IsWarning => Kind == NoticeKind.Warning;
    public bool IsError => Kind == NoticeKind.Error;
    public override string ToString() => $"{Time:HH:mm:ss}  {Text}";
}
