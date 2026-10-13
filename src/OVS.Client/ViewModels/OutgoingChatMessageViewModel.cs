using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OVS.Client.ViewModels;

/// <summary>
/// Package 98 (A113): an own message shown at once, greyed with a clock until the server's echo; refused or without an
/// echo within 10 s it is marked "nicht gesendet" and can be sent again.
/// </summary>
internal sealed partial class OutgoingChatMessageViewModel : ObservableObject
{
    readonly Func<PendingActionViewModel, Task> send;

    /// <summary>An own message on its way: pending waits for the server, send sends it again.</summary>
    public OutgoingChatMessageViewModel(PendingActionViewModel pending, Func<PendingActionViewModel, Task> send)
    {
        Pending = pending;
        this.send = send;
        pending.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not (nameof(Pending.IsRunning) or nameof(Pending.Error))) return;
            OnPropertyChanged(nameof(IsWaiting));
            OnPropertyChanged(nameof(IsFailed));
        };
    }

    /// <summary>The sending, running until the server's echo.</summary>
    public PendingActionViewModel Pending { get; }
    /// <summary>The message waits for the server.</summary>
    public bool IsWaiting => Pending.IsRunning;
    /// <summary>The server refused it or did not answer.</summary>
    public bool IsFailed => Pending.HasError;

    [RelayCommand]
    Task RetryAsync() => IsWaiting ? Task.CompletedTask : send(Pending);
}
