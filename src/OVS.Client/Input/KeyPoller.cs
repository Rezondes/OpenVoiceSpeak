using System.Runtime.InteropServices;

namespace OVS.Client.Input;

/// <summary>
/// Global push-to-talk keys via GetAsyncKeyState polling (10 ms). RegisterHotKey cannot report key release.
/// </summary>
public sealed partial class KeyPoller : IDisposable
{
    public const int VkXButton1 = 0x05;
    public const int VkXButton2 = 0x06;

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int vKey);

    readonly CancellationTokenSource cts = new();
    readonly Thread thread;
    TaskCompletionSource<int>? capture;

    public KeyPoller()
    {
        thread = new Thread(Loop) { IsBackground = true, Name = "KeyPoller" };
        thread.Start();
    }

    public int PttKey { get; set; } = VkXButton1;
    public int LinkPttKey { get; set; } = VkXButton2;
    public bool PttDown { get; private set; }
    public bool LinkPttDown { get; private set; }

    public event Action? Changed;

    static bool IsDown(int vk) => vk != 0 && (GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>Resolves with the next pressed key (left and right mouse button excluded).</summary>
    public Task<int> CaptureNextKeyAsync()
    {
        capture = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        return capture.Task;
    }

    void Loop()
    {
        var wasDown = new bool[256];
        while (!cts.IsCancellationRequested)
        {
            if (capture is { } pending)
            {
                for (int vk = 3; vk < 255; vk++)
                {
                    bool down = IsDown(vk);
                    if (down && !wasDown[vk])
                    {
                        capture = null;
                        pending.TrySetResult(vk);
                    }
                    wasDown[vk] = down;
                }
            }

            bool ptt = IsDown(PttKey), link = IsDown(LinkPttKey);
            if (ptt != PttDown || link != LinkPttDown)
            {
                PttDown = ptt;
                LinkPttDown = link;
                Changed?.Invoke();
            }
            Thread.Sleep(10);
        }
    }

    public static string KeyName(int vk) => vk switch
    {
        0 => "(keine)",
        VkXButton1 => "Maustaste 4",
        VkXButton2 => "Maustaste 5",
        0x04 => "Mittlere Maustaste",
        >= 0x70 and <= 0x87 => "F" + (vk - 0x6F),
        >= 0x30 and <= 0x5A => ((char)vk).ToString(),
        0x10 => "Umschalt",
        0x11 => "Strg",
        0x12 => "Alt",
        0x14 => "Feststelltaste",
        0x20 => "Leertaste",
        0xA0 => "Umschalt links",
        0xA1 => "Umschalt rechts",
        0xA2 => "Strg links",
        0xA3 => "Strg rechts",
        0xA4 => "Alt links",
        0xA5 => "Alt rechts",
        _ => $"Taste 0x{vk:X2}",
    };

    public void Dispose()
    {
        cts.Cancel();
        thread.Join(200);
        cts.Dispose();
    }
}
