using OVS.Client.Interop;

namespace OVS.Client.Input;

/// <summary>
/// Global key bindings via GetAsyncKeyState polling (10 ms). RegisterHotKey cannot report key release.
/// Works while another program (a game) has the focus.
/// </summary>
public sealed class KeyPoller : IDisposable
{
    readonly CancellationTokenSource cts = new();
    readonly Thread thread;
    readonly object gate = new();
    KeyActionSet simulated;
    readonly Func<int, bool> keyState;
    TaskCompletionSource<KeyChord>? capture;
    volatile IReadOnlyList<KeyBinding> bindings = [];
    volatile int down; // a KeyActionSet's bits: one write publishes a whole poll
    readonly TimeProvider time;
    long pttReleased, linkPttReleased, releaseDelay; // Package 130: time stamps of the last release (0: never), ticks

    /// <param name="time">Package 130: the clock of the release delay (tests use a manual one).</param>
    /// <param name="keyState">
    /// Package 121: the polled state of a virtual key, the keyboard by default. Tests pass a fake one, so a key goes
    /// through the bindings like a real one (<see cref="Simulate(KeyAction, bool)"/> skips them).
    /// </param>
    public KeyPoller(TimeProvider time, Func<int, bool>? keyState = null)
    {
        this.keyState = keyState ?? IsKeyDown;
        this.time = time;
        thread = new Thread(Loop) { IsBackground = true, Name = "KeyPoller" };
        thread.Start();
    }

    /// <summary>No bindings by default: new profiles start without any key (Package 29).</summary>
    public IReadOnlyList<KeyBinding> Bindings
    {
        get => bindings;
        set => bindings = value;
    }

    /// <summary>
    /// Package 130 (A139): how long push-to-talk and link push-to-talk still count as down after their key went up, so
    /// the last syllable is not cut. Push-to-mute never waits. Set by the engine from the settings.
    /// </summary>
    public TimeSpan ReleaseDelay
    {
        get => TimeSpan.FromTicks(Interlocked.Read(ref releaseDelay));
        set => Interlocked.Exchange(ref releaseDelay, value.Ticks);
    }

    /// <summary>Whether the action's key is down right now, without the release delay.</summary>
    public bool IsDown(KeyAction action) => new KeyActionSet(down).Contains(action);
    /// <summary>The talk keys with the release delay: the engine, the self test and the "Sendet" text read these.</summary>
    public bool PttDown => TalkDown(KeyAction.PushToTalk, ref pttReleased);
    public bool LinkPttDown => TalkDown(KeyAction.LinkPushToTalk, ref linkPttReleased);
    public bool MuteHeld => IsDown(KeyAction.PushToMute);

    bool TalkDown(KeyAction action, ref long releasedAt)
    {
        if (IsDown(action)) return true;
        long at = Interlocked.Read(ref releasedAt);
        return at != 0 && time.GetElapsedTime(at) < ReleaseDelay;
    }

    /// <summary>A hold action (PTT, Link-PTT, Push-to-Mute) went down or up. Raised on the polling thread.</summary>
    public event Action? Changed;

    /// <summary>A toggle action was pressed, once per press. Raised on the polling thread.</summary>
    public event Action<KeyAction>? Pressed;

    /// <summary>Holds an action's key down in software (debug API, tests).</summary>
    public void Simulate(KeyAction action, bool isDown)
    {
        lock (gate)
        {
            simulated = isDown ? simulated.With(action) : simulated.Without(action);
        }
    }

    /// <summary>Shorthand for the two talk keys. Null leaves a key unchanged.</summary>
    public void Simulate(bool? ptt = null, bool? linkPtt = null)
    {
        if (ptt is { } p) Simulate(KeyAction.PushToTalk, p);
        if (linkPtt is { } l) Simulate(KeyAction.LinkPushToTalk, l);
    }

    static bool IsKeyDown(int vk) => vk != 0 && (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

    ChordModifiers HeldModifiers() =>
        (keyState(VirtualKeys.Control) ? ChordModifiers.Ctrl : 0) |
        (keyState(VirtualKeys.Shift) ? ChordModifiers.Shift : 0) |
        (keyState(VirtualKeys.Menu) ? ChordModifiers.Alt : 0);

    /// <summary>
    /// Resolves with the next pressed key plus the modifiers held with it (left and right mouse button excluded).
    /// Package 125: Shift, Ctrl or Alt alone counts as the key once let go, if no other key was pressed meanwhile.
    /// </summary>
    public Task<KeyChord> CaptureNextChordAsync()
    {
        capture = new TaskCompletionSource<KeyChord>(TaskCreationOptions.RunContinuationsAsynchronously);
        return capture.Task;
    }

    void Loop()
    {
        var wasDown = new bool[256];
        var tracker = new KeyStateTracker();
        TaskCompletionSource<KeyChord>? primedFor = null;
        int lone = 0; // the modifier pressed alone during a capture; -1 once a second one joined it, until all are up
        while (!cts.IsCancellationRequested)
        {
            var held = HeldModifiers();
            if (capture is { } pending)
            {
                // The first poll of a capture only records what is already down (e.g. Enter that pressed "Belegen").
                bool primed = primedFor == pending;
                if (!primed || (lone < 0 && held == ChordModifiers.None)) lone = 0;
                for (int vk = 3; vk < 255; vk++)
                {
                    bool isDown = keyState(vk);
                    if (primed && isDown != wasDown[vk])
                    {
                        KeyChord? chord = null;
                        if (!VirtualKeys.IsModifier(vk)) chord = isDown ? new KeyChord(vk, held) : null;
                        else if (VirtualKeys.IsSidedModifier(vk) && isDown) lone = lone == 0 ? vk : -1;
                        else if (VirtualKeys.IsSidedModifier(vk) && lone == vk) chord = new KeyChord(vk);
                        if (chord is not null)
                        {
                            capture = null;
                            pending.TrySetResult(chord);
                        }
                    }
                    wasDown[vk] = isDown;
                }
                primedFor = pending;
            }

            var active = KeyBindingMatcher.Resolve(bindings, keyState, held);
            lock (gate) active = new KeyActionSet(active.Bits | simulated.Bits);
            var (pressed, holdChanged) = tracker.Update(active);
            // Package 130: the release time goes first, so a reader never sees the key up without it
            var released = new KeyActionSet(down & ~active.Bits);
            if (released.Contains(KeyAction.PushToTalk)) Interlocked.Exchange(ref pttReleased, time.GetTimestamp());
            if (released.Contains(KeyAction.LinkPushToTalk)) Interlocked.Exchange(ref linkPttReleased, time.GetTimestamp());
            down = active.Bits;
            if (holdChanged) Changed?.Invoke();
            for (int index = 0; index < KeyActionTraits.All.Count; index++)
                if (pressed.Contains(KeyActionTraits.All[index])) Pressed?.Invoke(KeyActionTraits.All[index]);
            Thread.Sleep(10); // clock: real time
        }
    }

    public void Dispose()
    {
        cts.Cancel();
        thread.Join(200); // clock: real time
        cts.Dispose();
    }
}
