namespace OVS.Manager.Servers;

/// <summary>Package M18 (AC4): a server whose window is open and the version it gets.</summary>
/// <param name="ServerId">The server's id.</param>
/// <param name="Version">The release it is updated to.</param>
internal sealed record UpdateDue(string ServerId, string Version);
