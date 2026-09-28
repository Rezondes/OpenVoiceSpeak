using OVS.Client.Localization;
using System.Runtime.InteropServices;

namespace OVS.Client.Input;

/// <summary>
/// Global key bindings via GetAsyncKeyState polling (10 ms). RegisterHotKey cannot report key release.
/// Works while another program (a game) has the focus.
/// </summary>
public sealed partial class KeyPoller : IDisposable
{
    public const int VkXButton1 = 0x05;
    public const int VkXButton2 = 0x06;
    const int VkShift = 0x10, VkControl = 0x11, VkMenu = 0x12;

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int vKey);

    readonly CancellationTokenSource cts = new();
    readonly Thread thread;
    readonly object gate = new();
    readonly HashSet<KeyAction> simulated = [];
    TaskCompletionSource<KeyChord>? capture;
    volatile IReadOnlyList<KeyBinding> bindings = [];
    volatile HashSet<KeyAction> down = [];

    public KeyPoller()
    {
        thread = new Thread(Loop) { IsBackground = true, Name = "KeyPoller" };
        thread.Start();
    }

    /// <summary>No bindings by default: new profiles start without any key (Package 29).</summary>
    public IReadOnlyList<KeyBinding> Bindings
    {
        get => bindings;
        set => bindings = value;
    }

    public bool IsDown(KeyAction action) => down.Contains(action);
    public bool PttDown => IsDown(KeyAction.PushToTalk);
    public bool LinkPttDown => IsDown(KeyAction.LinkPushToTalk);
    public bool MuteHeld => IsDown(KeyAction.PushToMute);

    /// <summary>A hold action (PTT, Link-PTT, Push-to-Mute) went down or up. Raised on the polling thread.</summary>
    public event Action? Changed;

    /// <summary>A toggle action was pressed, once per press. Raised on the polling thread.</summary>
    public event Action<KeyAction>? Pressed;

    /// <summary>Holds an action's key down in software (debug API, tests).</summary>
    public void Simulate(KeyAction action, bool isDown)
    {
        lock (gate)
        {
            if (isDown) simulated.Add(action);
            else simulated.Remove(action);
        }
    }

    /// <summary>Shorthand for the two talk keys. Null leaves a key unchanged.</summary>
    public void Simulate(bool? ptt = null, bool? linkPtt = null)
    {
        if (ptt is { } p) Simulate(KeyAction.PushToTalk, p);
        if (linkPtt is { } l) Simulate(KeyAction.LinkPushToTalk, l);
    }

    static bool IsKeyDown(int vk) => vk != 0 && (GetAsyncKeyState(vk) & 0x8000) != 0;

    static bool IsModifier(int vk) => vk is VkShift or VkControl or VkMenu or (>= 0xA0 and <= 0xA5);

    static ChordModifiers HeldModifiers() =>
        (IsKeyDown(VkControl) ? ChordModifiers.Ctrl : 0) |
        (IsKeyDown(VkShift) ? ChordModifiers.Shift : 0) |
        (IsKeyDown(VkMenu) ? ChordModifiers.Alt : 0);

    /// <summary>Resolves with the next pressed key plus the modifiers held with it (left and right mouse button excluded).</summary>
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
        while (!cts.IsCancellationRequested)
        {
            var held = HeldModifiers();
            if (capture is { } pending)
            {
                // The first poll of a capture only records what is already down (e.g. Enter that pressed "Belegen").
                bool primed = primedFor == pending;
                for (int vk = 3; vk < 255; vk++)
                {
                    bool isDown = IsKeyDown(vk);
                    if (primed && isDown && !wasDown[vk] && !IsModifier(vk))
                    {
                        capture = null;
                        pending.TrySetResult(new KeyChord(vk, held));
                    }
                    wasDown[vk] = isDown;
                }
                primedFor = pending;
            }

            var active = KeyActions.Resolve(bindings, IsKeyDown, held);
            lock (gate) active.UnionWith(simulated);
            var (pressed, holdChanged) = tracker.Update(active);
            down = active;
            if (holdChanged) Changed?.Invoke();
            foreach (var action in pressed) Pressed?.Invoke(action);
            Thread.Sleep(10);
        }
    }

    public static string KeyName(int vk) => vk switch
    {
        0 => Strings.Key_None,
        VkXButton1 => Strings.Key_Mouse4,
        VkXButton2 => Strings.Key_Mouse5,
        0x04 => Strings.Key_MouseMiddle,
        >= 0x70 and <= 0x87 => "F" + (vk - 0x6F),
        >= 0x30 and <= 0x5A => ((char)vk).ToString(),
        0x10 => Strings.Key_Shift,
        0x11 => Strings.Key_Ctrl,
        0x12 => "Alt",
        0x14 => Strings.Key_CapsLock,
        0x20 => Strings.Key_Space,
        0xA0 => Strings.Key_LeftShift,
        0xA1 => Strings.Key_RightShift,
        0xA2 => Strings.Key_LeftCtrl,
        0xA3 => Strings.Key_RightCtrl,
        0xA4 => Strings.Key_LeftAlt,
        0xA5 => Strings.Key_RightAlt,
        _ => string.Format(Strings.Key_Unknown, vk),
    };

    public void Dispose()
    {
        cts.Cancel();
        thread.Join(200);
        cts.Dispose();
    }
}
