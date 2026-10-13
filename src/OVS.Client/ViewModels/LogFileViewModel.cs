using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using OVS.Client.Collections;
using OVS.Client.Localization;
using OVS.Shared.Protocol.Messages.LogAccess;

namespace OVS.Client.ViewModels;

/// <summary>One file of the list: server or channel name, start and size; Package 82: a checkbox with LogsDownload.</summary>
internal sealed partial class LogFileViewModel(LogFileInfo info, bool canSelect = false, bool isSelected = false, Action<LogFileViewModel>? selectionChanged = null)
    : ObservableObject, IMotionKey
{
    /// <summary>The file's id: the same file keeps its motion when the list is rebuilt.</summary>
    public object MotionKey => Info.Id;
    [ObservableProperty] bool isSelected = isSelected;

    partial void OnIsSelectedChanged(bool value) => selectionChanged?.Invoke(this);

    /// <summary>The file can be ticked for a download.</summary>
    public bool CanSelect { get; } = canSelect;
    /// <summary>The file as the server lists it.</summary>
    public LogFileInfo Info { get; } = info;
    /// <summary>The source: the server or the channel's name.</summary>
    public string Title => TitleOf(Info);
    /// <summary>A server log.</summary>
    public bool IsServer => Info.Kind == LogKind.Server;
    /// <summary>The start, local time.</summary>
    public string StartText => Info.Start.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    /// <summary>Start and size.</summary>
    public string Details => $"{StartText}, {Formats.Size(Info.Size)}";

    /// <summary>A log file's source as shown.</summary>
    public static string TitleOf(LogFileInfo logFile) => logFile.Kind == LogKind.Server ? Strings.Ui_LogSourceServer : logFile.ChannelName ?? Strings.Ui_LogDeletedChannel;
}
