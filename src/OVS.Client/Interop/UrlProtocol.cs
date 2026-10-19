using OVS.Shared.Net;
using System.Security;
using Microsoft.Win32;
using OVS.Client.Net;

namespace OVS.Client.Interop;

/// <summary>
/// Package 170 (A172): registers <c>ovs://</c> for the current Windows user under HKCU\Software\Classes\ovs, pointing at
/// the running exe, so a server link starts OVS; needs no admin rights. Written only when the command differs, so a
/// moved exe updates it and an unchanged one is left alone.
/// </summary>
internal static class UrlProtocol
{
    const string KeyPath = @"Software\Classes\" + JoinLink.Scheme;
    const string CommandPath = KeyPath + @"\shell\open\command";

    /// <summary>The command Windows runs for a link: the exe with the link as its one argument.</summary>
    public static string CommandFor(string exe) => $"\"{exe}\" \"%1\"";

    /// <summary>Registers <paramref name="exe"/>; true when the registration was written now, false when it was already right.</summary>
    /// <exception cref="PlatformNotSupportedException">Not on Windows.</exception>
    /// <exception cref="UnauthorizedAccessException">The key cannot be written.</exception>
    /// <exception cref="SecurityException">The key cannot be written.</exception>
    /// <exception cref="IOException">The key cannot be written.</exception>
    public static bool Register(string exe)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The ovs:// link is registered on Windows only.");
        using var command = Registry.CurrentUser.CreateSubKey(CommandPath);
        if (command.GetValue("") as string == CommandFor(exe)) return false;
        using (var root = Registry.CurrentUser.CreateSubKey(KeyPath))
        {
            root.SetValue("", "URL:OpenVoiceSpeak");
            root.SetValue("URL Protocol", "");
        }
        command.SetValue("", CommandFor(exe));
        return true;
    }
}
