using OVS.Shared.Permissions;
using OVS.Shared.Protocol;

namespace OVS.Client;

public static class ErrorTexts
{
    static readonly Dictionary<string, string> Texts = new()
    {
        [Codes.VersionMismatch] = "Client und Server verwenden unterschiedliche Protokollversionen.",
        [Codes.BadSignature] = "Die Anmeldung mit deiner Identität ist fehlgeschlagen.",
        [Codes.WrongPassword] = "Falsches Serverpasswort.",
        [Codes.ServerFull] = "Der Server ist voll.",
        [Codes.NicknameInvalid] = "Ungültiger Nickname (1 bis 32 Zeichen).",
        [Codes.NicknameTaken] = "Dieser Nickname ist bereits online.",
        [Codes.TooManyConnections] = "Zu viele Verbindungen von deiner IP-Adresse.",
        [Codes.Timeout] = "Zeitüberschreitung.",
        [Codes.Banned] = "Du bist auf diesem Server gebannt.",
        [Codes.ProtocolError] = "Protokollfehler.",
        [Codes.ReplacedByNewConnection] = "Du hast dich von woanders mit derselben Identität verbunden.",
        [Codes.ServerShutdown] = "Der Server wurde heruntergefahren.",
        [Codes.Kicked] = "Du wurdest vom Server gekickt.",
        [Codes.ConnectionLost] = "Die Verbindung zum Server ist abgebrochen.",
        [Codes.PermissionDenied] = "Dafür fehlt dir das Recht.",
        [Codes.NotFound] = "Nicht gefunden (vielleicht inzwischen gelöscht).",
        [Codes.InvalidName] = "Ungültiger Name.",
        [Codes.NameTaken] = "Dieser Name ist bereits vergeben.",
        [Codes.InvalidValue] = "Ungültiger Wert.",
        [Codes.CannotDeleteDefault] = "Der Standard-Channel kann nicht gelöscht werden.",
        [Codes.InvalidToken] = "Das Admin-Token ist ungültig oder wurde bereits eingelöst.",
        [Codes.ProtectedGroup] = "Diese Gruppe ist geschützt.",
        [Codes.LastAdmin] = "Der letzte Admin kann nicht entfernt werden.",
        [Codes.InvalidLink] = "Ein Channel kann nicht mit sich selbst verlinkt werden.",
        [Codes.UnknownRequest] = "Der Server kennt diese Anfrage nicht.",
    };

    public static bool Has(string code) => Texts.ContainsKey(code);

    public static string For(string code, string? detail = null)
    {
        var text = Texts.TryGetValue(code, out var t) ? t : $"Fehler: {code}";
        return string.IsNullOrWhiteSpace(detail) ? text : $"{text} ({detail})";
    }
}

public static class PermissionLabels
{
    public static readonly IReadOnlyList<(Permission Permission, string Label)> All =
    [
        (Permission.Speak, "Sprechen"),
        (Permission.SpeakLinked, "Über Links sprechen"),
        (Permission.ChannelCreate, "Channels anlegen"),
        (Permission.ChannelEdit, "Channels bearbeiten"),
        (Permission.ChannelDelete, "Channels löschen"),
        (Permission.ChannelLink, "Channels verlinken"),
        (Permission.UserMove, "Nutzer verschieben"),
        (Permission.UserMute, "Nutzer stummschalten"),
        (Permission.UserKick, "Nutzer kicken"),
        (Permission.UserBan, "Nutzer bannen"),
        (Permission.GroupsManage, "Gruppen verwalten"),
        (Permission.GroupsAssign, "Gruppen zuweisen"),
        (Permission.ServerConfig, "Servereinstellungen ändern"),
    ];
}
