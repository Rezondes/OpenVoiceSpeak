namespace OVS.Manager.Servers;

/// <summary>Package M10: a server or a manager that can be ticked in the assignments.</summary>
/// <param name="Id">The server's or the account's id.</param>
/// <param name="Name">Its name.</param>
internal sealed record AssignmentChoice(string Id, string Name);
