namespace OVS.Client.Models;

/// <summary>How the ban list is sorted.</summary>
internal enum BanSortOrder
{
    /// <summary>The newest ban first.</summary>
    Newest,
    /// <summary>The ban that ends first first.</summary>
    EndingSoonest,
    /// <summary>The ban that turned most people away first.</summary>
    MostAttempts,
    /// <summary>By nickname.</summary>
    Name,
}
