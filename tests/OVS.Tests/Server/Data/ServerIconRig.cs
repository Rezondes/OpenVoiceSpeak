using OVS.Tests.TestSupport;

namespace OVS.Tests.Server.Data;

/// <summary>A started test server with an admin and a guest connected.</summary>
/// <param name="Server">The running server.</param>
/// <param name="Admin">A client in the admin group.</param>
/// <param name="Guest">A client without rights.</param>
internal sealed record ServerIconRig(TestServer Server, TestClient Admin, TestClient Guest);
