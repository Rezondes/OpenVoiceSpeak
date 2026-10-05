namespace OVS.Client.Dialogs;

/// <param name="Password">The password shown in the dialog; stored only with SavePassword.</param>
public sealed record BookmarkEdit(string Name, string Host, int Port, string Nickname, string? Password, bool SavePassword);
