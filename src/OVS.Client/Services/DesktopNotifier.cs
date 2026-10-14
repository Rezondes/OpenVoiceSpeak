using OVS.Client.Interop;
using static OVS.Client.Interop.NativeMethods;

namespace OVS.Client.Services;

/// <summary>
/// Package 162: shows a desktop notification as the balloon of a tray icon of the main window (Windows 10 and 11 show
/// it as a toast). No window of its own (A20) and no package (A166). The icon is added with the first notification and
/// removed on dispose; a click on the notification comes back as a message to the main window.
/// </summary>
internal sealed unsafe class DesktopNotifier : IDisposable
{
    /// <summary>The message the shell sends to the main window for the icon (WM_APP + 0x162).</summary>
    public const uint CallbackMessage = 0x8000 + 0x162;
    /// <summary>The user clicked the balloon (in the callback's low word of lParam).</summary>
    public const int BalloonClicked = 0x405;

    const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_INFO = 0x10;
    const uint NIIF_INFO = 0x1, NIIF_NOSOUND = 0x10; // OVS plays its own tones (Package 56)
    const int SM_CXSMICON = 49;

    readonly nint window;
    readonly Func<uint, NOTIFYICONDATAW, bool> shell;
    readonly nint icon;
    bool added;
    DesktopNotice? shown;

    /// <param name="window">The main window's handle; it receives the clicks.</param>
    /// <param name="shell">Shell_NotifyIconW; the tests pass a fake.</param>
    public DesktopNotifier(nint window, Func<uint, NOTIFYICONDATAW, bool>? shell = null)
    {
        this.window = window;
        this.shell = shell ?? ((message, data) => Shell_NotifyIconW(message, &data) != 0);
        if (shell is null) icon = AppIcon();
    }

    /// <summary>A click on the notification shown last.</summary>
    public event Action<DesktopNotice>? Clicked;

    /// <summary>Shows the notification, replacing the one before.</summary>
    public void Show(DesktopNotice notice)
    {
        var data = Data();
        if (!added)
        {
            data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
            Copy("OpenVoiceSpeak", data.szTip, 128);
            added = shell(NIM_ADD, data);
            if (!added) return;
        }
        data.uFlags = NIF_INFO;
        data.dwInfoFlags = NIIF_INFO | NIIF_NOSOUND;
        Copy(notice.Title, data.szInfoTitle, 64);
        Copy(notice.Text, data.szInfo, 256);
        if (shell(NIM_MODIFY, data)) shown = notice;
    }

    /// <summary>A message to the main window; true when it was the icon's (a click on the notification is told).</summary>
    public bool OnWindowMessage(uint message, nint lParam)
    {
        if (message != CallbackMessage) return false;
        if ((lParam & 0xFFFF) == BalloonClicked && shown is { } notice) Clicked?.Invoke(notice);
        return true;
    }

    /// <summary>The title of a balloon as the shell gets it (for the tests).</summary>
    internal static string TitleOf(NOTIFYICONDATAW data) => new(data.szInfoTitle);

    /// <summary>Removes the tray icon.</summary>
    public void Dispose()
    {
        if (added) _ = shell(NIM_DELETE, Data());
        added = false;
        if (icon != 0) _ = DestroyIcon(icon);
    }

    NOTIFYICONDATAW Data() => new() { cbSize = (uint)sizeof(NOTIFYICONDATAW), hWnd = window, uID = 1, uCallbackMessage = CallbackMessage, hIcon = icon };

    static void Copy(string text, char* target, int capacity)
    {
        int length = Math.Min(text.Length, capacity - 1);
        text.AsSpan(0, length).CopyTo(new Span<char>(target, capacity));
        target[length] = '\0';
    }

    /// <summary>The program's own icon at the small icon size, 0 when it has none.</summary>
    static nint AppIcon()
    {
        nint small = 0;
        if (Environment.ProcessPath is not { } path) return 0;
        fixed (char* file = path)
        {
            #pragma warning disable CA1508 // the icon is written through a pointer the analyzer does not follow
            return SHDefExtractIconW(file, 0, 0, &small, null, (uint)GetSystemMetrics(SM_CXSMICON)) == 0 ? small : 0;
            #pragma warning restore CA1508
        }
    }
}
