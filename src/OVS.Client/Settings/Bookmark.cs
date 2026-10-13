namespace OVS.Client.Settings;

/// <param name="ProtectedPassword">Package 39: the server password, DPAPI-protected for the current Windows user.</param>
/// <param name="Name">The bookmark's name.</param>
/// <param name="Host">The server's host.</param>
/// <param name="Port">The server's port.</param>
/// <param name="Nickname">The nickname to connect with.</param>
internal sealed record Bookmark(string Name, string Host, int Port, string Nickname, string? ProtectedPassword = null)
{
    /// <summary>A password is stored and readable.</summary>
    public bool HasSavedPassword => SavedPassword() is not null;

    /// <summary>Null when none is stored or it cannot be read here (another Windows user or PC, damaged value).</summary>
    public string? SavedPassword() => PasswordProtector.Unprotect(ProtectedPassword);
}
