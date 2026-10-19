using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using OVS.Manager.Accounts;
using OVS.Manager.Audit;

namespace OVS.Manager.Data;

/// <summary>
/// Package M1: the manager's SQLite database in <see cref="ManagerOptions.DatabasePath"/>, migrated at every start.
/// Package M3: with the accounts and roles of ASP.NET Core Identity. Package M4: with the signed-in sessions.
/// </summary>
internal sealed class ManagerDbContext(DbContextOptions<ManagerDbContext> options) : IdentityDbContext<ManagerUser>(options)
{
    /// <summary>Package M4: the signed-in browsers of the accounts.</summary>
    public DbSet<ManagerSession> Sessions => Set<ManagerSession>();

    /// <summary>Package M5: the audit log.</summary>
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<AuditEntry>(entry =>
        {
            entry.Property(value => value.AtUtc).HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc)); // SQLite keeps no kind
            entry.HasIndex(value => value.AtUtc);
            entry.HasIndex(value => value.ServerId);
            entry.HasIndex(value => value.UserName);
        });
        builder.Entity<ManagerSession>(session =>
        {
            session.HasKey(value => value.Id);
            session.Property(value => value.UserAgent).HasMaxLength(SessionStore.MaxAgentLength);
            session.HasOne<ManagerUser>().WithMany().HasForeignKey(value => value.UserId).OnDelete(DeleteBehavior.Cascade); // a deleted account takes its sessions along
        });
    }
}
