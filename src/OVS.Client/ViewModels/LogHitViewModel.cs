using System.Globalization;
using OVS.Client.Localization;
using OVS.Shared.Protocol.Messages.LogAccess;

namespace OVS.Client.ViewModels;

/// <summary>One search hit: file title, line number and the line.</summary>
internal sealed class LogHitViewModel(LogHit hit, string title)
{
    /// <summary>The hit's file.</summary>
    public string FileId => hit.FileId;
    /// <summary>The hit's line number.</summary>
    public int Line => hit.Line;
    /// <summary>The line's text.</summary>
    public string Text => hit.Text;
    /// <summary>The file's source as shown.</summary>
    public string Title { get; } = title;
    /// <summary>The line number as shown.</summary>
    public string LineText => string.Format(CultureInfo.CurrentCulture, Strings.Ui_LogLine, Line);
}
