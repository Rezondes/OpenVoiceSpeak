namespace OVS.Client.Models;

/// <summary>Which bans the ban list shows.</summary>
public enum BanStatusFilter
{
    /// <summary>Only active bans.</summary>
    Active,
    /// <summary>Only expired bans.</summary>
    Expired,
    /// <summary>Only lifted bans.</summary>
    Lifted,
    /// <summary>Every ban.</summary>
    All,
}
