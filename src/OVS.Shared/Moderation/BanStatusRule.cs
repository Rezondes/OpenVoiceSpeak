namespace OVS.Shared.Moderation;

/// <summary>Package 80 (A97): the status of a ban, the same on the server (bans that block) and in the client's ban list.</summary>
public static class BanStatusRule
{
    /// <summary>Lifted wins; otherwise expired once the end is reached (an end equal to now is expired); no end = permanent.</summary>
    public static BanStatus Evaluate(DateTimeOffset? liftedAt, DateTimeOffset? expiresAt, DateTimeOffset now) =>
        liftedAt is not null ? BanStatus.Lifted : expiresAt is { } end && end <= now ? BanStatus.Expired : BanStatus.Active;
}
