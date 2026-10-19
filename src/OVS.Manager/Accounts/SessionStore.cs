using Microsoft.EntityFrameworkCore;
using OVS.Manager.Data;

namespace OVS.Manager.Accounts;

/// <summary>Package M4 (AC4): the signed-in sessions of the users; the login cookie is valid only while its session exists.</summary>
internal sealed class SessionStore(ManagerDbContext database, TimeProvider time)
{
    /// <summary>How long a session's last-seen time may lag behind, so not every request writes.</summary>
    public static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(1);

    /// <summary>The longest user agent kept.</summary>
    public const int MaxAgentLength = 200;

    /// <summary>A new session of <paramref name="userId"/>; returns its id for the cookie.</summary>
    public async Task<string> StartAsync(string userId, string? userAgent, string? address, CancellationToken cancellation = default)
    {
        var now = time.GetUtcNow();
        var session = new ManagerSession
        {
            Id = Guid.NewGuid().ToString("N"), UserId = userId, CreatedAt = now, LastSeenAt = now,
            UserAgent = userAgent is { Length: > MaxAgentLength } ? userAgent[..MaxAgentLength] : userAgent, Address = address,
        };
        database.Sessions.Add(session);
        await database.SaveChangesAsync(cancellation);
        return session.Id;
    }

    /// <summary>True while the session exists; its last-seen time moves on.</summary>
    public async Task<bool> IsActiveAsync(string id, CancellationToken cancellation = default)
    {
        var session = await database.Sessions.FindAsync([id], cancellation);
        if (session is null) return false;
        var now = time.GetUtcNow();
        if (now - session.LastSeenAt >= TouchInterval)
        {
            session.LastSeenAt = now;
            await database.SaveChangesAsync(cancellation);
        }
        return true;
    }

    /// <summary>The sessions of <paramref name="userId"/>, newest first.</summary>
    public async Task<List<ManagerSession>> ListAsync(string userId, CancellationToken cancellation = default) =>
        [.. (await database.Sessions.Where(session => session.UserId == userId).ToListAsync(cancellation)).OrderByDescending(session => session.CreatedAt)];

    /// <summary>Ends every session of <paramref name="userId"/> but <paramref name="keepId"/>.</summary>
    public Task EndOthersAsync(string userId, string keepId, CancellationToken cancellation = default) =>
        database.Sessions.Where(session => session.UserId == userId && session.Id != keepId).ExecuteDeleteAsync(cancellation);

    /// <summary>Ends every session of <paramref name="userId"/> (locked, reset or deleted).</summary>
    public Task EndAllAsync(string userId, CancellationToken cancellation = default) =>
        database.Sessions.Where(session => session.UserId == userId).ExecuteDeleteAsync(cancellation);
}
