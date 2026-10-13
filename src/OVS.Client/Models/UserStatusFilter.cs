namespace OVS.Client.Models;

/// <summary>Which users the user list shows.</summary>
public enum UserStatusFilter
{
    /// <summary>Everyone.</summary>
    All,
    /// <summary>Only users connected now.</summary>
    Online,
    /// <summary>Only users not connected.</summary>
    Offline,
    /// <summary>Only users with an active ban.</summary>
    Banned,
}
