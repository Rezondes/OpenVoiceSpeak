namespace OVS.Client.Models;

/// <summary>Which kind of bans the ban list shows.</summary>
internal enum BanTypeFilter
{
    /// <summary>Every kind.</summary>
    All,
    /// <summary>Only bans without an end.</summary>
    Permanent,
    /// <summary>Only bans with an end.</summary>
    Temporary,
    /// <summary>Only bans that cover an address.</summary>
    WithIp,
}
