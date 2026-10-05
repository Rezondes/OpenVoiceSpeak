using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OVS.Client.ViewModels;

/// <summary>
/// Package 98 (A113): an own message shown at once, greyed with a clock until the server's echo; refused or without an
/// echo within 10 s it is marked "nicht gesendet" and can be sent again.
/// </summary>
public sealed partial class ChatSendingViewModel : ObservableObject
{
    readonly Func<PendingViewModel, Task> send;

    public ChatSendingViewModel(PendingViewModel pending, Func<PendingViewModel, Task> send)
    {
        Pending = pending;
        this.send = send;
        pending.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not (nameof(Pending.IsRunning) or nameof(Pending.Error))) return;
            OnPropertyChanged(nameof(IsWaiting));
            OnPropertyChanged(nameof(IsFailed));
        };
    }

    public PendingViewModel Pending { get; }
    public bool IsWaiting => Pending.IsRunning;
    public bool IsFailed => Pending.HasError;

    [RelayCommand]
    Task Retry() => IsWaiting ? Task.CompletedTask : send(Pending);
}
