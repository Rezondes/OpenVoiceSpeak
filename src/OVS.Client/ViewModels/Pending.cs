using CommunityToolkit.Mvvm.ComponentModel;
using OVS.Client.Localization;

namespace OVS.Client.ViewModels;

/// <summary>
/// Package 97 (A113): one action that waits for the server. Start marks it running at once (callers ignore a second try
/// meanwhile); IsBusy, which the spinner and the disabled button follow, comes only after 150 ms, so a fast answer never
/// flickers and a slow one shows within 400 ms. Done or Fail end it; without either within 10 s it fails with
/// "Keine Antwort vom Server" and can be started again.
/// </summary>
/// <param name="post">Runs the timers' work on the UI thread; inline when null (tests with a manual clock).</param>
public sealed partial class Pending(TimeProvider time, Action<Action>? post = null) : ObservableObject
{
    public static readonly TimeSpan ShowAfter = TimeSpan.FromMilliseconds(150);
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    ITimer? show, timeout;
    int round;
    TaskCompletionSource<string?>? completion;

    [ObservableProperty] bool isRunning;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsFirstLoad), nameof(IsRefreshing), nameof(HasState))] bool isBusy;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasError), nameof(IsFirstLoadFailed), nameof(IsRefreshFailed), nameof(HasState))] string? error;
    /// <summary>Done came at least once: a list shows content and a new request only refreshes it.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsFirstLoad), nameof(IsRefreshing), nameof(IsFirstLoadFailed), nameof(IsRefreshFailed))]
    bool hasSucceeded;

    public bool HasError => Error is not null;
    /// <summary>Busy or failed: something to show, so the place for it takes no room otherwise.</summary>
    public bool HasState => IsBusy || HasError;
    /// <summary>A list's first load failed: the error in place of the list.</summary>
    public bool IsFirstLoadFailed => HasError && !HasSucceeded;
    /// <summary>A refresh failed: the old content stays, the error beside it.</summary>
    public bool IsRefreshFailed => HasError && HasSucceeded;

    /// <summary>A list's first load: the spinner and "Wird geladen ..." in place of the list.</summary>
    public bool IsFirstLoad => IsBusy && !HasSucceeded;
    /// <summary>A list's refresh: the old content stays, a small spinner beside it.</summary>
    public bool IsRefreshing => IsBusy && HasSucceeded;

    /// <summary>The request the answer or error is matched by, null when not running.</summary>
    public string? RequestId { get; private set; }

    /// <summary>The 10 s passed without an answer (e.g. to put back what was shown ahead of the answer).</summary>
    public event Action? TimedOut;

    /// <summary>Package 98: how the current run ended: null when done, else the error (refused or timed out).</summary>
    public Task<string?> Completion => completion?.Task ?? Task.FromResult(Error);

    public void Start(string? requestId = null)
    {
        Stop();
        if (completion is not { Task.IsCompleted: false }) completion = new(); // a retry while running keeps who waits
        var mine = round;
        RequestId = requestId;
        Error = null;
        IsRunning = true;
        show = time.CreateTimer(_ => OnUi(mine, () => IsBusy = true), null, ShowAfter, System.Threading.Timeout.InfiniteTimeSpan);
        timeout = time.CreateTimer(_ => OnUi(mine, () =>
        {
            Fail(Strings.Pending_NoAnswer);
            TimedOut?.Invoke();
        }), null, Timeout, System.Threading.Timeout.InfiniteTimeSpan);
    }

    /// <summary>Whether this is running for the request with that id.</summary>
    public bool Answers(string? requestId) => IsRunning && requestId is not null && requestId == RequestId;

    /// <summary>
    /// The answer or the awaited state change arrived; for a list also when it came without being asked for. A late one
    /// after the timeout clears that error, the action happened after all.
    /// </summary>
    public void Done()
    {
        HasSucceeded = true;
        Stop();
        Error = null;
        completion?.TrySetResult(null);
    }

    /// <summary>Ends a running action with a visible error; the action can be started again.</summary>
    public void Fail(string error)
    {
        if (!IsRunning) return;
        Stop();
        Error = error;
        completion?.TrySetResult(error);
    }

    /// <summary>Package 98: the flag of a row (a channel, a user) follows IsBusy.</summary>
    public void Show(Action<bool> busy) => PropertyChanged += (_, e) =>
    {
        if (e.PropertyName == nameof(IsBusy)) busy(IsBusy);
    };

    /// <summary>Package 98: on Avalonia's UI thread, work goes back there; elsewhere (view model tests) it runs inline.</summary>
    public static Action<Action> UiPost() => SynchronizationContext.Current is Avalonia.Threading.AvaloniaSynchronizationContext ui
        ? action => ui.Post(_ => action(), null)
        : action => action();

    void Stop()
    {
        show?.Dispose();
        timeout?.Dispose();
        show = timeout = null;
        round++;
        RequestId = null;
        IsRunning = false;
        IsBusy = false;
    }

    void OnUi(int mine, Action action)
    {
        void Run()
        {
            if (mine == round && IsRunning) action();
        }
        if (post is null) Run();
        else post(Run);
    }
}
