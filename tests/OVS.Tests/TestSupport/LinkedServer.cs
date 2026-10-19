namespace OVS.Tests.TestSupport;

/// <summary>Package M12: an in-process server the manager holds its service session to.</summary>
/// <param name="Id">The server's id in the manager.</param>
/// <param name="Server">The running server.</param>
internal sealed record LinkedServer(string Id, TestServer Server) : IAsyncDisposable
{
    /// <summary>Stops the server.</summary>
    public ValueTask DisposeAsync() => Server.DisposeAsync();
}
