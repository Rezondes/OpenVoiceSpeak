namespace OVS.Shared.Protocol.Messages.Connection;

/// <summary>The first message of the handshake: who connects and with which protocol version.</summary>
/// <param name="ProtocolVersion">The client's <see cref="WireProtocol.Version"/>.</param>
/// <param name="Nickname">The name the user wants on this server.</param>
/// <param name="PublicKey">The identity's public key, base64; its SHA-256 is the fingerprint.</param>
/// <param name="Password">The server password, null for none.</param>
/// <param name="Service">Package 191 (A203): the server admin connects invisibly, as the web manager does; the client never sets it.</param>
public sealed record ClientHello(int ProtocolVersion, string Nickname, string PublicKey, string? Password, bool Service = false) : Message;
