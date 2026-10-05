namespace OVS.Shared.Protocol.Messages.Users;

/// <param name="Offset">Package 87: every list comes in pages; Offset is the first entry wanted, the server picks the page size.</param>
public sealed record ListUsers(int Offset = 0) : Request;
