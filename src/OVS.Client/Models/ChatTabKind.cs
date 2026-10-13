namespace OVS.Client.Models;

/// <summary>The kind of a chat tab.</summary>
internal enum ChatTabKind
{
    /// <summary>The tab of "Allgemein", to everyone on the server.</summary>
    General,
    /// <summary>The tab of the own channel.</summary>
    Channel,
    /// <summary>A tab with one user.</summary>
    Private,
}
