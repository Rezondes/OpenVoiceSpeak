namespace OVS.Client.Dialogs;

public sealed record ConnectChoice(string Host, int Port, string Nickname, string? Password, bool SaveBookmark, bool SavePassword = false);
