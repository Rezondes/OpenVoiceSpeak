namespace OVS.Manager.Docker;

/// <summary>Package M6: a container of the manager as Docker lists it.</summary>
/// <param name="Id">Docker's container id.</param>
/// <param name="ServerId">The server's id (label ovs.server-id).</param>
/// <param name="Port">The server's port (label ovs.port).</param>
/// <param name="Version">The server's version (label ovs.version).</param>
/// <param name="State">Docker's state: created, running, exited, restarting and so on.</param>
/// <param name="Image">The image with its tag.</param>
internal sealed record ManagedContainer(string Id, string ServerId, int Port, string Version, string State, string Image);
