namespace OVS.Shared.Protocol;

/// <summary>Every code the server can put into Error, Rejected or Disconnected.</summary>
public static class Codes
{
    // Rejected (handshake)
    public const string VersionMismatch = nameof(VersionMismatch);
    public const string BadSignature = nameof(BadSignature);
    public const string WrongPassword = nameof(WrongPassword);
    public const string ServerFull = nameof(ServerFull);
    public const string NicknameInvalid = nameof(NicknameInvalid);
    public const string NicknameTaken = nameof(NicknameTaken);
    public const string TooManyConnections = nameof(TooManyConnections);
    public const string Timeout = nameof(Timeout);
    public const string Banned = nameof(Banned);
    public const string ProtocolError = nameof(ProtocolError);

    // Disconnected
    public const string ReplacedByNewConnection = nameof(ReplacedByNewConnection);
    public const string ServerShutdown = nameof(ServerShutdown);
    public const string Kicked = nameof(Kicked);
    public const string ConnectionLost = nameof(ConnectionLost); // client side only

    // Error (requests)
    public const string PermissionDenied = nameof(PermissionDenied);
    public const string NotFound = nameof(NotFound);
    public const string InvalidName = nameof(InvalidName);
    public const string NameTaken = nameof(NameTaken);
    public const string InvalidValue = nameof(InvalidValue);
    public const string CannotDeleteDefault = nameof(CannotDeleteDefault);
    public const string InvalidToken = nameof(InvalidToken);
    public const string ProtectedGroup = nameof(ProtectedGroup);
    public const string LastAdmin = nameof(LastAdmin);
    public const string InvalidLink = nameof(InvalidLink);
    public const string UnknownRequest = nameof(UnknownRequest);

    public static IEnumerable<string> All() =>
        typeof(Codes).GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!);
}
