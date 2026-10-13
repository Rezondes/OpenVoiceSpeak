namespace OVS.Shared.Moderation;

/// <summary>Where a ban stands at a given time; <see cref="BanStatusRule"/> decides it for the server and the client.</summary>
public enum BanStatus
{
    /// <summary>The ban holds now.</summary>
    Active,
    /// <summary>The ban's time ran out.</summary>
    Expired,
    /// <summary>A moderator lifted the ban.</summary>
    Lifted,
}
