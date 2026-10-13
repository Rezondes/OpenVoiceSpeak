namespace OVS.Client.Models;

/// <summary>How the user list is sorted.</summary>
public enum UserSortOrder
{
    /// <summary>By nickname.</summary>
    Name,
    /// <summary>The newest login first.</summary>
    LastLogin,
    /// <summary>The longest online time first.</summary>
    OnlineTime,
}
