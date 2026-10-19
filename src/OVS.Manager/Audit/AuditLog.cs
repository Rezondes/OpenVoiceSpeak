using Microsoft.EntityFrameworkCore;
using OVS.Manager.Data;

namespace OVS.Manager.Audit;

/// <summary>Package M5: writes the audit lines and reads them filtered; a manager reads the lines of their servers only.</summary>
internal sealed class AuditLog(ManagerDbContext database, TimeProvider time)
{
    /// <summary>The most lines one query returns, newest first.</summary>
    public const int MaxLines = 1000;

    /// <summary>One line, now.</summary>
    public async Task WriteAsync(string userName, string action, string? target = null, string? serverId = null, string? detail = null, CancellationToken cancellation = default)
    {
        database.AuditEntries.Add(new AuditEntry
        {
            AtUtc = time.GetUtcNow().UtcDateTime, UserName = userName, Action = action, Target = target, ServerId = serverId, Detail = detail,
        });
        await database.SaveChangesAsync(cancellation);
    }

    /// <summary>
    /// The lines matching <paramref name="filter"/>, newest first, at most <see cref="MaxLines"/>. <paramref name="servers"/>
    /// limits them to those servers (a manager's assigned ones, M10); null means every line (the admin).
    /// </summary>
    public Task<List<AuditEntry>> QueryAsync(AuditFilter filter, IReadOnlySet<string>? servers, CancellationToken cancellation = default)
    {
        var lines = database.AuditEntries.AsNoTracking();
        if (servers is not null) lines = lines.Where(entry => entry.ServerId != null && servers.Contains(entry.ServerId));
        if (filter.UserName is { Length: > 0 } user) lines = lines.Where(entry => entry.UserName == user);
        if (filter.ServerId is { Length: > 0 } server) lines = lines.Where(entry => entry.ServerId == server);
        if (filter.Action is { Length: > 0 } action) lines = lines.Where(entry => entry.Action == action);
        if (filter.FromUtc is { } from) lines = lines.Where(entry => entry.AtUtc >= from);
        if (filter.ToUtc is { } to) lines = lines.Where(entry => entry.AtUtc < to);
        return lines.OrderByDescending(entry => entry.Id).Take(MaxLines).ToListAsync(cancellation);
    }

    /// <summary>Package M5 (AC4): deletes the lines older than <paramref name="age"/>; returns how many.</summary>
    public Task<int> DeleteOlderThanAsync(TimeSpan age, CancellationToken cancellation = default)
    {
        var before = (time.GetUtcNow() - age).UtcDateTime;
        return database.AuditEntries.Where(entry => entry.AtUtc < before).ExecuteDeleteAsync(cancellation);
    }
}
