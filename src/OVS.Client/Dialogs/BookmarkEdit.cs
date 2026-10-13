namespace OVS.Client.Dialogs;

/// <param name="Password">The password shown in the dialog; stored only with SavePassword.</param>
/// <param name="Name">The bookmark's name.</param>
/// <param name="Host">The server's host.</param>
/// <param name="Port">The server's port.</param>
/// <param name="Nickname">The nickname to connect with.</param>
/// <param name="SavePassword">Store the password.</param>
public sealed record BookmarkEdit(string Name, string Host, int Port, string Nickname, string? Password, bool SavePassword);
