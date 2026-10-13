using System.Security.Cryptography;
using System.Text;

namespace OVS.Client.Settings;

/// <summary>Windows DPAPI for the current user (A35): readable only by this Windows account on this PC.</summary>
internal static class PasswordProtector
{
    static readonly byte[] Entropy = "OpenVoiceSpeak server password"u8.ToArray();

    /// <summary>The password protected with DPAPI for the current Windows user, as base64.</summary>
    public static string Protect(string password)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Passwords can only be stored on Windows.");
        return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(password), Entropy, DataProtectionScope.CurrentUser));
    }

    /// <summary>The password back, null when it cannot be read (another user or machine).</summary>
    public static string? Unprotect(string? protectedPassword)
    {
        if (string.IsNullOrEmpty(protectedPassword) || !OperatingSystem.IsWindows()) return null;
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedPassword), Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            return null;
        }
    }
}
