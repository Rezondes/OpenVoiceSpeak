using System.Runtime.InteropServices;

namespace OVS.Client.Interop;

/// <summary>Every Win32 function the client imports, each declared once.</summary>
internal static unsafe partial class NativeMethods
{
    /// <summary>Package 162: adds, changes or removes the tray icon and its balloon.</summary>
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] // CA5392: a system library, never one planted next to the exe
    [LibraryImport("shell32.dll")] internal static partial int Shell_NotifyIconW(uint message, NOTIFYICONDATAW* data);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] // CA5392: a system library, never one planted next to the exe
    [LibraryImport("shell32.dll")] internal static partial int SHDefExtractIconW(char* file, int index, uint flags, nint* large, nint* small, uint size);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] // CA5392: a system library, never one planted next to the exe
    [LibraryImport("user32.dll")] internal static partial nint SetThreadDpiAwarenessContext(nint context);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] // CA5392: a system library, never one planted next to the exe
    [LibraryImport("user32.dll")] internal static partial uint GetDpiForSystem();
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] // CA5392: a system library, never one planted next to the exe
    [LibraryImport("user32.dll")] internal static partial int GetSystemMetrics(int index);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] // CA5392: a system library, never one planted next to the exe
    [LibraryImport("user32.dll")] internal static partial nint GetDC(nint hwnd);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] // CA5392: a system library, never one planted next to the exe
    [LibraryImport("user32.dll")] internal static partial int ReleaseDC(nint hwnd, nint dc);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] // CA5392: a system library, never one planted next to the exe
    [LibraryImport("gdi32.dll")] internal static partial nint CreateCompatibleDC(nint dc);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] // CA5392: a system library, never one planted next to the exe
    [LibraryImport("gdi32.dll")] internal static partial nint CreateDIBSection(nint dc, BITMAPINFOHEADER* info, uint usage, out nint bits, nint section, uint offset);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] // CA5392: a system library, never one planted next to the exe
    [LibraryImport("gdi32.dll")] internal static partial nint SelectObject(nint dc, nint obj);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] // CA5392: a system library, never one planted next to the exe
    [LibraryImport("gdi32.dll")] internal static partial int DeleteDC(nint dc);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] // CA5392: a system library, never one planted next to the exe
    [LibraryImport("gdi32.dll")] internal static partial int DeleteObject(nint obj);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] // CA5392: a system library, never one planted next to the exe
    [LibraryImport("user32.dll")] internal static partial int DrawIconEx(nint dc, int left, int top, nint icon, int width, int height, uint step, nint brush, uint flags);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] // CA5392: a system library, never one planted next to the exe
    [LibraryImport("user32.dll")] internal static partial int DestroyIcon(nint icon);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] // CA5392: a system library, never one planted next to the exe
    [LibraryImport("kernel32.dll")] internal static partial nint GetModuleHandleW(char* name);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] // CA5392: a system library, never one planted next to the exe
    [LibraryImport("user32.dll")] internal static partial nint LoadCursorW(nint instance, nint name);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] // CA5392: a system library, never one planted next to the exe
    [LibraryImport("user32.dll")] internal static partial ushort RegisterClassExW(WNDCLASSEXW* type);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] // CA5392: a system library, never one planted next to the exe
    [LibraryImport("user32.dll")] internal static partial int UnregisterClassW(char* name, nint instance);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] // CA5392: a system library, never one planted next to the exe
    [LibraryImport("user32.dll")] internal static partial nint CreateWindowExW(uint exStyle, char* type, char* title, uint style, int left, int top, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] // CA5392: a system library, never one planted next to the exe
    [LibraryImport("user32.dll")] internal static partial int UpdateLayeredWindow(nint hwnd, nint dst, POINT* at, SIZE* size, nint src, POINT* origin, uint key, BLENDFUNCTION* blend, uint flags);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] // CA5392: a system library, never one planted next to the exe
    [LibraryImport("user32.dll")] internal static partial int ShowWindow(nint hwnd, int command);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] // CA5392: a system library, never one planted next to the exe
    [LibraryImport("user32.dll")] internal static partial nuint SetTimer(nint hwnd, nuint id, uint milliseconds, nint callback);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] // CA5392: a system library, never one planted next to the exe
    [LibraryImport("user32.dll")] internal static partial int GetMessageW(MSG* message, nint hwnd, uint min, uint max);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] // CA5392: a system library, never one planted next to the exe
    [LibraryImport("user32.dll")] internal static partial nint DispatchMessageW(MSG* message);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] // CA5392: a system library, never one planted next to the exe
    [LibraryImport("user32.dll")] internal static partial int PostMessageW(nint hwnd, uint message, nint wParam, nint lParam);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] // CA5392: a system library, never one planted next to the exe
    [LibraryImport("user32.dll")] internal static partial int DestroyWindow(nint hwnd);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] // CA5392: a system library, never one planted next to the exe
    [LibraryImport("user32.dll")] internal static partial void PostQuitMessage(int code);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] // CA5392: a system library, never one planted next to the exe
    [LibraryImport("user32.dll")] internal static partial nint DefWindowProcW(nint hwnd, uint message, nint wParam, nint lParam);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] // CA5392: a system library, never one planted next to the exe
    [LibraryImport("user32.dll")]
    internal static partial short GetAsyncKeyState(int vKey);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] // CA5392: a system library, never one planted next to the exe
    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SystemParametersInfo(uint action, uint param, out int value, uint winIni);
}
