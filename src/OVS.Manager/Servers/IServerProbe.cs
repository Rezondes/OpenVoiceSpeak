namespace OVS.Manager.Servers;

/// <summary>Package M8 (AC4): whether a server answers on its port; the tests use a fake.</summary>
internal interface IServerProbe
{
    /// <summary>True when <paramref name="host"/> finishes a TLS handshake on <paramref name="port"/>.</summary>
    Task<bool> AnswersAsync(string host, int port, CancellationToken cancellation = default);
}
