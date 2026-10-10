namespace OVS.Client.Dialogs;

public sealed record ConnectChoice(string Host, int Port, string Nickname, string? Password, bool SaveBookmark, bool SavePassword = false)
{
    /// <summary>The password to send: null when none was given (an empty one counts as none).</summary>
    public string? PasswordOrNull => string.IsNullOrEmpty(Password) ? null : Password;
}
