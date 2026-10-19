using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OVS.Manager.Audit;
using OVS.Manager.Data;

namespace OVS.Manager.Api;

/// <summary>
/// Package M31 (AC1): makes, lists, finds and revokes API tokens. A secret is 32 random bytes, shown once; the manager keeps
/// only its SHA-256, enough for a secret with that much chance in it.
/// </summary>
internal sealed class ApiTokenStore(ManagerDbContext database, AuditLog audit, TimeProvider time)
{
    /// <summary>The longest name.</summary>
    public const int MaxNameLength = 50;

    /// <summary>The most days a token is valid.</summary>
    public const int MaxDays = 3650;

    /// <summary>The start of every secret, so a leaked one is recognised.</summary>
    public const string Prefix = "ovs_";

    /// <summary>Makes a token named <paramref name="name"/> for <paramref name="userId"/> valid for <paramref name="days"/> days; returns its secret, or null for an empty or too long name or a day count out of 1 to <see cref="MaxDays"/>.</summary>
    public async Task<string?> CreateAsync(string userId, string userName, string name, int days, CancellationToken cancellation = default)
    {
        name = name.Trim();
        if (name.Length is 0 or > MaxNameLength || days is < 1 or > MaxDays) return null;
        var secret = Prefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var now = time.GetUtcNow().UtcDateTime;
        database.ApiTokens.Add(new ApiToken
        {
            Id = Guid.NewGuid().ToString("N"), UserId = userId, Name = name, Hash = HashOf(secret), CreatedAtUtc = now, ExpiresAtUtc = now.AddDays(days),
        });
        await database.SaveChangesAsync(cancellation);
        await audit.WriteAsync(userName, AuditActions.ApiTokenCreated, name, cancellation: cancellation);
        return secret;
    }

    /// <summary>The tokens of <paramref name="userId"/>, newest first.</summary>
    public async Task<IReadOnlyList<ApiToken>> ListAsync(string userId, CancellationToken cancellation = default) =>
        [.. (await database.ApiTokens.AsNoTracking().Where(token => token.UserId == userId).ToListAsync(cancellation)).OrderByDescending(token => token.CreatedAtUtc)];

    /// <summary>Removes the token <paramref name="tokenId"/> of <paramref name="userId"/>; false when there is none.</summary>
    public async Task<bool> RevokeAsync(string userId, string tokenId, string userName, CancellationToken cancellation = default)
    {
        var token = await database.ApiTokens.SingleOrDefaultAsync(row => row.Id == tokenId && row.UserId == userId, cancellation);
        if (token is null) return false;
        database.ApiTokens.Remove(token);
        await database.SaveChangesAsync(cancellation);
        await audit.WriteAsync(userName, AuditActions.ApiTokenRevoked, token.Name, cancellation: cancellation);
        return true;
    }

    /// <summary>The token whose secret is <paramref name="secret"/> while it is valid, else null.</summary>
    public async Task<ApiToken?> FindAsync(string secret, CancellationToken cancellation = default)
    {
        if (!secret.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        var hash = HashOf(secret);
        var now = time.GetUtcNow().UtcDateTime;
        return await database.ApiTokens.AsNoTracking().SingleOrDefaultAsync(token => token.Hash == hash && token.ExpiresAtUtc > now, cancellation);
    }

    /// <summary>The stored form of <paramref name="secret"/>.</summary>
    public static string HashOf(string secret) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
}
