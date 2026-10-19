namespace OVS.Tests.TestSupport;

/// <summary>Package M11: a server row of the manager and the fingerprint of the manager's identity for it.</summary>
/// <param name="Id">The server's id.</param>
/// <param name="AdminFingerprint">The manager's identity on that server, its server admin.</param>
internal sealed record AddedServer(string Id, string AdminFingerprint);
