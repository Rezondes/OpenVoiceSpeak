using OVS.Client.Localization;
using OVS.Shared.Protocol.Messages.LogAccess;

namespace OVS.Client.ViewModels;

/// <summary>One search hit: file title, line number and the line.</summary>
public sealed class LogHitViewModel(LogHit hit, string title)
{
    public string FileId => hit.FileId;
    public int Line => hit.Line;
    public string Text => hit.Text;
    public string Title { get; } = title;
    public string LineText => string.Format(Strings.Ui_LogLine, Line);
}
