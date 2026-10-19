using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using OVS.Manager.Accounts;

namespace OVS.Manager.Data;

/// <summary>
/// Package M1: the manager's SQLite database in <see cref="ManagerOptions.DatabasePath"/>, migrated at every start.
/// Package M3: with the accounts and roles of ASP.NET Core Identity. Package M4: with the signed-in sessions.
/// </summary>
internal sealed class ManagerDbContext(DbContextOptions<ManagerDbContext> options) : IdentityDbContext<ManagerUser>(options)
{
    /// <summary>Package M4: the signed-in browsers of the accounts.</summary>
    public DbSet<ManagerSession> Sessions => Set<ManagerSession>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<ManagerSession>(session =>
        {
            session.HasKey(value => value.Id);
            session.Property(value => value.UserAgent).HasMaxLength(SessionStore.MaxAgentLength);
            session.HasOne<ManagerUser>().WithMany().HasForeignKey(value => value.UserId).OnDelete(DeleteBehavior.Cascade); // a deleted account takes its sessions along
        });
    }
}
