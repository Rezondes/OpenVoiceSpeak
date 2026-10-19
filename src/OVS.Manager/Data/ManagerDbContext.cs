using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using OVS.Manager.Accounts;
using OVS.Manager.Audit;
using OVS.Manager.Servers;

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

    /// <summary>Package M7: the servers the manager created.</summary>
    public DbSet<ManagedServer> Servers => Set<ManagedServer>();

    /// <summary>Package M7 (MA4): the manager's identity per server, the private key encrypted.</summary>
    public DbSet<ServerIdentity> ServerIdentities => Set<ServerIdentity>();

    /// <summary>Package M10: which manager may operate which server.</summary>
    public DbSet<ServerAssignment> ServerAssignments => Set<ServerAssignment>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<ManagedServer>(server =>
        {
            server.HasKey(value => value.Id);
            server.HasIndex(value => value.Port).IsUnique(); // D2: one server per port
            server.Property(value => value.CreatedAtUtc).HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
            server.Property(value => value.DeletedAtUtc).HasConversion(value => value, value => value == null ? null : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
        });
        builder.Entity<ServerIdentity>(identity => identity.HasKey(value => value.ServerId));
        builder.Entity<ServerAssignment>(assignment =>
        {
            assignment.HasKey(value => new { value.UserId, value.ServerId });
            assignment.HasIndex(value => value.ServerId);
            assignment.HasOne<ManagerUser>().WithMany().HasForeignKey(value => value.UserId).OnDelete(DeleteBehavior.Cascade); // a deleted account takes its assignments along
            assignment.HasOne<ManagedServer>().WithMany().HasForeignKey(value => value.ServerId).OnDelete(DeleteBehavior.Cascade); // and so does a purged server (M9)
        });
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
