using OVS.Client.Dialogs;
using OVS.Client.Net;

namespace OVS.Client.Services;

/// <summary>
/// How a connection is opened for every connect and every reconnect attempt: the real one is the profile's identity plus
/// <see cref="ClientConnection.ConnectAsync"/>, tests pass their own.
/// </summary>
internal delegate Task<ClientConnection> OpenConnection(ConnectChoice choice, CancellationToken token);
