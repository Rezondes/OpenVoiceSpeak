using System.Collections.Concurrent;
using OVS.Manager.Servers;

namespace OVS.Tests.TestSupport;

/// <summary>Package M8 (AC4): every server answers the TLS handshake unless its host is in <see cref="Silent"/>.</summary>
internal sealed class FakeServerProbe : IServerProbe
{
    /// <summary>The hosts (container names ovs-&lt;id&gt;) that do not answer.</summary>
    public ConcurrentDictionary<string, byte> Silent { get; } = new();

    public Task<bool> AnswersAsync(string host, int port, CancellationToken cancellation = default) => Task.FromResult(!Silent.ContainsKey(host));
}
