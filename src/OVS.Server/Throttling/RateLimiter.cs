namespace OVS.Server.Throttling;

/// <summary>Token bucket per session: voice packets by default, Package 86 also uses it for requests.</summary>
public sealed class RateLimiter(TimeProvider time, double perSecond = 60, double burst = 10)
{
    readonly double perSecond = perSecond, burst = burst;
    double tokens = burst;
    long last = time.GetTimestamp();

    public bool TryTake()
    {
        long now = time.GetTimestamp();
        tokens = Math.Min(burst, tokens + time.GetElapsedTime(last, now).TotalSeconds * perSecond);
        last = now;
        if (tokens < 1) return false;
        tokens -= 1;
        return true;
    }
}
