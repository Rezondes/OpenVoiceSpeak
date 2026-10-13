using System.Runtime.InteropServices;

namespace OVS.Client.Interop;

/// <summary>Every Win32 function the client imports, each declared once.</summary>
internal static unsafe partial class NativeMethods
{
    [LibraryImport("shell32.dll")] internal static partial int SHDefExtractIconW(char* file, int index, uint flags, nint* large, nint* small, uint size);
    [LibraryImport("user32.dll")] internal static partial nint SetThreadDpiAwarenessContext(nint context);
    [LibraryImport("user32.dll")] internal static partial uint GetDpiForSystem();
    [LibraryImport("user32.dll")] internal static partial int GetSystemMetrics(int index);
    [LibraryImport("user32.dll")] internal static partial nint GetDC(nint hwnd);
    [LibraryImport("user32.dll")] internal static partial int ReleaseDC(nint hwnd, nint dc);
    [LibraryImport("gdi32.dll")] internal static partial nint CreateCompatibleDC(nint dc);
    [LibraryImport("gdi32.dll")] internal static partial nint CreateDIBSection(nint dc, BITMAPINFOHEADER* info, uint usage, out nint bits, nint section, uint offset);
    [LibraryImport("gdi32.dll")] internal static partial nint SelectObject(nint dc, nint obj);
    [LibraryImport("gdi32.dll")] internal static partial int DeleteDC(nint dc);
    [LibraryImport("gdi32.dll")] internal static partial int DeleteObject(nint obj);
    [LibraryImport("user32.dll")] internal static partial int DrawIconEx(nint dc, int x, int y, nint icon, int w, int h, uint step, nint brush, uint flags);
    [LibraryImport("user32.dll")] internal static partial int DestroyIcon(nint icon);
    [LibraryImport("kernel32.dll")] internal static partial nint GetModuleHandleW(char* name);
    [LibraryImport("user32.dll")] internal static partial nint LoadCursorW(nint instance, nint name);
    [LibraryImport("user32.dll")] internal static partial ushort RegisterClassExW(WNDCLASSEXW* type);
    [LibraryImport("user32.dll")] internal static partial int UnregisterClassW(char* name, nint instance);
    [LibraryImport("user32.dll")] internal static partial nint CreateWindowExW(uint exStyle, char* type, char* title, uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);
    [LibraryImport("user32.dll")] internal static partial int UpdateLayeredWindow(nint hwnd, nint dst, POINT* at, SIZE* size, nint src, POINT* origin, uint key, BLENDFUNCTION* blend, uint flags);
    [LibraryImport("user32.dll")] internal static partial int ShowWindow(nint hwnd, int command);
    [LibraryImport("user32.dll")] internal static partial nuint SetTimer(nint hwnd, nuint id, uint milliseconds, nint callback);
    [LibraryImport("user32.dll")] internal static partial int GetMessageW(MSG* message, nint hwnd, uint min, uint max);
    [LibraryImport("user32.dll")] internal static partial nint DispatchMessageW(MSG* message);
    [LibraryImport("user32.dll")] internal static partial int PostMessageW(nint hwnd, uint message, nint wParam, nint lParam);
    [LibraryImport("user32.dll")] internal static partial int DestroyWindow(nint hwnd);
    [LibraryImport("user32.dll")] internal static partial void PostQuitMessage(int code);
    [LibraryImport("user32.dll")] internal static partial nint DefWindowProcW(nint hwnd, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    internal static partial short GetAsyncKeyState(int vKey);

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SystemParametersInfo(uint action, uint param, out int value, uint winIni);
}
