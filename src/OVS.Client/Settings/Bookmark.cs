namespace OVS.Client.Settings;

/// <param name="ProtectedPassword">Package 39: the server password, DPAPI-protected for the current Windows user.</param>
public sealed record Bookmark(string Name, string Host, int Port, string Nickname, string? ProtectedPassword = null)
{
    public bool HasSavedPassword => SavedPassword() is not null;

    /// <summary>Null when none is stored or it cannot be read here (another Windows user or PC, damaged value).</summary>
    public string? SavedPassword() => PasswordProtector.Unprotect(ProtectedPassword);
}
