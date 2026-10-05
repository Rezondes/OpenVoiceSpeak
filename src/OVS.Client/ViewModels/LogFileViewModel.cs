using CommunityToolkit.Mvvm.ComponentModel;
using OVS.Client.Collections;
using OVS.Client.Localization;
using OVS.Shared.Protocol.Messages.LogAccess;

namespace OVS.Client.ViewModels;

/// <summary>One file of the list: server or channel name, start and size; Package 82: a checkbox with LogsDownload.</summary>
public sealed partial class LogFileViewModel(LogFileInfo info, bool canSelect = false, bool isSelected = false, Action<LogFileViewModel>? selectionChanged = null)
    : ObservableObject, IMotionKey
{
    public object MotionKey => Info.Id;
    [ObservableProperty] bool isSelected = isSelected;

    partial void OnIsSelectedChanged(bool value) => selectionChanged?.Invoke(this);

    public bool CanSelect { get; } = canSelect;
    public LogFileInfo Info { get; } = info;
    public string Title => TitleOf(Info);
    public bool IsServer => Info.Kind == LogKind.Server;
    public string StartText => Info.Start.ToLocalTime().ToString("g");
    public string Details => $"{StartText}, {BackupViewModel.Size(Info.Size)}";

    public static string TitleOf(LogFileInfo f) => f.Kind == LogKind.Server ? Strings.Ui_LogSourceServer : f.ChannelName ?? Strings.Ui_LogDeletedChannel;
}
