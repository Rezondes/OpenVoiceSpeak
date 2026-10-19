using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using OVS.Manager.Accounts;
using OVS.Manager.Audit;
using OVS.Manager.Control;
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

    /// <summary>Package M15: the backup schedule of each server.</summary>
    public DbSet<BackupSchedule> BackupSchedules => Set<BackupSchedule>();

    /// <summary>Package M15: the backups the manager made for a reason.</summary>
    public DbSet<BackupMark> BackupMarks => Set<BackupMark>();

    /// <summary>Package M28: the manager's own settings.</summary>
    public DbSet<ManagerSetting> ManagerSettings => Set<ManagerSetting>();

    /// <summary>Package M27: what each user wants to be told, and how.</summary>
    public DbSet<Notifications.NotificationRule> NotificationRules => Set<Notifications.NotificationRule>();

    /// <summary>Package M27: the events sent and not yet over.</summary>
    public DbSet<Notifications.AlertState> AlertStates => Set<Notifications.AlertState>();

    /// <summary>Package M27: the servers' thresholds.</summary>
    public DbSet<Notifications.ServerAlertSettings> ServerAlertSettings => Set<Notifications.ServerAlertSettings>();

    /// <summary>Package M18: the maintenance window of each server with automatic updates.</summary>
    public DbSet<UpdateWindow> UpdateWindows => Set<UpdateWindow>();

    /// <summary>Package M31: the users' API tokens.</summary>
    public DbSet<Api.ApiToken> ApiTokens => Set<Api.ApiToken>();

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
        builder.Entity<BackupSchedule>(schedule =>
        {
            schedule.HasKey(value => value.ServerId);
            schedule.Property(value => value.LastRunUtc).HasConversion(value => value, value => value == null ? null : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
            schedule.HasOne<ManagedServer>().WithMany().HasForeignKey(value => value.ServerId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<Notifications.NotificationRule>(rule =>
        {
            rule.HasIndex(value => new { value.UserId, value.Event, value.Channel }).IsUnique();
            rule.Property(value => value.LastErrorAtUtc).HasConversion(value => value, value => value == null ? null : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
            rule.HasOne<ManagerUser>().WithMany().HasForeignKey(value => value.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<ManagerSetting>().HasKey(setting => setting.Key);
        builder.Entity<Api.ApiToken>(token =>
        {
            token.HasKey(value => value.Id);
            token.HasIndex(value => value.Hash).IsUnique();
            token.Property(value => value.CreatedAtUtc).HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
            token.Property(value => value.ExpiresAtUtc).HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
            token.HasOne<ManagerUser>().WithMany().HasForeignKey(value => value.UserId).OnDelete(DeleteBehavior.Cascade); // a deleted account takes its tokens along
        });
        builder.Entity<Notifications.AlertState>(state =>
        {
            state.HasKey(value => new { value.Key, value.Event });
            state.Property(value => value.SentAtUtc).HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        });
        builder.Entity<Notifications.ServerAlertSettings>(settings =>
        {
            settings.HasKey(value => value.ServerId);
            settings.HasOne<ManagedServer>().WithMany().HasForeignKey(value => value.ServerId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<UpdateWindow>(window =>
        {
            window.HasKey(value => value.ServerId);
            window.Property(value => value.LastRunUtc).HasConversion(value => value, value => value == null ? null : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
            window.HasOne<ManagedServer>().WithMany().HasForeignKey(value => value.ServerId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<BackupMark>(mark =>
        {
            mark.HasKey(value => new { value.ServerId, value.FileName });
            mark.HasOne<ManagedServer>().WithMany().HasForeignKey(value => value.ServerId).OnDelete(DeleteBehavior.Cascade);
        });
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
