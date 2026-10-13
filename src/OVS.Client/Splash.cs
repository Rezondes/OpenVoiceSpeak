using System.Diagnostics;
using System.Runtime.InteropServices;
using OVS.Client.Interop;
using static OVS.Client.Interop.NativeMethods;

namespace OVS.Client;

/// <summary>
/// The logo shows the moment the exe starts, while Avalonia and the main window are still being built (a cold start
/// takes a few seconds). A plain Win32 layered window on its own thread, there before any Avalonia code runs, with the
/// busy cursor over it. The one OS window besides the main window (A20); it fades out once the
/// main window has drawn its first frame.
/// </summary>
public static unsafe class Splash
{
    const int Size = 160; // at 96 dpi
    const double FadeSeconds = 0.15;

    static readonly Lock gate = new();
    static nint window;
    static bool closed;
    static long fadeStart;

    /// <summary>Shows the logo (the exe's icon) centred on the primary screen; the thread ends once it is closed.</summary>
    public static Thread Show(string? iconFile = null)
    {
        lock (gate)
        {
            window = 0;
            closed = false;
            fadeStart = 0;
        }
        var thread = new Thread(() => Run(iconFile ?? Environment.ProcessPath)) { IsBackground = true, Name = "Splash" };
        thread.Start();
        return thread;
    }

    /// <summary>Fades the logo out; also before it was shown (then it never shows).</summary>
    public static void Close()
    {
        lock (gate)
        {
            closed = true;
            if (window != 0) PostMessageW(window, WM_CLOSE, 0, 0);
        }
    }

    /// <summary>Whether the logo is on the screen now (for the tests).</summary>
    public static bool IsShown
    {
        get
        {
            lock (gate) return window != 0;
        }
    }

    static void Run(string? iconFile)
    {
        SetThreadDpiAwarenessContext(-4); // per monitor v2, only this thread: the logo is sharp at any scale
        int px = (int)(Size * GetDpiForSystem() / 96);
        nint icon = 0;
        if (iconFile is null) return;
        fixed (char* path = iconFile)
        {
            if (SHDefExtractIconW(path, 0, 0, &icon, null, (uint)px) != 0 || icon == 0) return; // no icon: no splash
        }

        nint screen = GetDC(0), memory = CreateCompatibleDC(screen);
        var header = new BITMAPINFOHEADER { biSize = (uint)sizeof(BITMAPINFOHEADER), biWidth = px, biHeight = -px, biPlanes = 1, biBitCount = 32 };
        nint bitmap = CreateDIBSection(memory, &header, 0, out _, 0, 0);
        SelectObject(memory, bitmap);
        DrawIconEx(memory, 0, 0, icon, px, px, 0, 0, DI_NORMAL); // keeps the icon's alpha in the 32 bit picture

        fixed (char* name = "OvsSplash")
        {
            var type = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WndProc,
                hInstance = GetModuleHandleW(null),
                hCursor = LoadCursorW(0, IDC_APPSTARTING),
                lpszClassName = name,
            };
            RegisterClassExW(&type);
            var at = new POINT { screenX = (GetSystemMetrics(SM_CXSCREEN) - px) / 2, screenY = (GetSystemMetrics(SM_CYSCREEN) - px) / 2 };
            nint hwnd = CreateWindowExW(WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, name, name, WS_POPUP,
                at.screenX, at.screenY, px, px, 0, 0, type.hInstance, 0);
            var size = new SIZE { cx = px, cy = px };
            var origin = new POINT();
            var blend = new BLENDFUNCTION { SourceConstantAlpha = 255, AlphaFormat = AC_SRC_ALPHA };
            UpdateLayeredWindow(hwnd, screen, &at, &size, memory, &origin, 0, &blend, ULW_ALPHA);
            lock (gate)
            {
                window = hwnd;
                if (closed) PostMessageW(hwnd, WM_CLOSE, 0, 0); // closed while it was being made
            }
            ShowWindow(hwnd, SW_SHOWNA); // leaves the activation to the main window
            MSG message;
            while (GetMessageW(&message, 0, 0, 0) > 0) DispatchMessageW(&message);
            lock (gate) window = 0;
            UnregisterClassW(name, type.hInstance);
        }
        DeleteDC(memory);
        DeleteObject(bitmap);
        ReleaseDC(0, screen);
        DestroyIcon(icon);
    }

    [UnmanagedCallersOnly]
    static nint WndProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case WM_TIMER: // fading out
                double faded = Stopwatch.GetElapsedTime(fadeStart).TotalSeconds / FadeSeconds; // clock: frame timing
                if (faded >= 1)
                {
                    DestroyWindow(hwnd);
                    return 0;
                }
                var blend = new BLENDFUNCTION { SourceConstantAlpha = (byte)(255 * (1 - faded)), AlphaFormat = AC_SRC_ALPHA };
                UpdateLayeredWindow(hwnd, 0, null, null, 0, null, 0, &blend, ULW_ALPHA); // only the strength changes
                return 0;
            case WM_CLOSE:
                if (fadeStart == 0)
                {
                    fadeStart = Stopwatch.GetTimestamp(); // clock: frame timing
                    SetTimer(hwnd, 1, 16, 0); // clock: frame timing
                }
                return 0;
            case WM_DESTROY:
                PostQuitMessage(0);
                return 0;
        }
        return DefWindowProcW(hwnd, message, wParam, lParam);
    }

    // ---- Win32 ----

    const uint WM_DESTROY = 0x0002, WM_CLOSE = 0x0010, WM_TIMER = 0x0113;
    const uint WS_POPUP = 0x80000000, WS_EX_LAYERED = 0x00080000, WS_EX_TOOLWINDOW = 0x00000080, WS_EX_NOACTIVATE = 0x08000000;
    const int SM_CXSCREEN = 0, SM_CYSCREEN = 1, SW_SHOWNA = 8, IDC_APPSTARTING = 32650;
    const uint DI_NORMAL = 3, ULW_ALPHA = 2;
    const byte AC_SRC_ALPHA = 1;
}
