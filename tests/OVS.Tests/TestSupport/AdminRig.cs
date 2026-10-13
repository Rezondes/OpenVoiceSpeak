namespace OVS.Tests.TestSupport;

/// <summary>A started test server with an admin connected to it.</summary>
/// <param name="Server">The running server.</param>
/// <param name="Admin">A client in the admin group.</param>
public sealed record AdminRig(TestServer Server, TestClient Admin);
