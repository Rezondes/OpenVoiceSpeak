using Microsoft.EntityFrameworkCore;

namespace OVS.Manager.Telemetry;

/// <summary>
/// Package M20 (MA7): the servers' telemetry in a SQLite database of its own, <see cref="ManagerOptions.TelemetryPath"/>,
/// so its size and deletion (M21) never touch the manager's data; migrated at every start.
/// </summary>
internal sealed class TelemetryStore(DbContextOptions<TelemetryStore> options) : DbContext(options)
{
    /// <summary>The metric points.</summary>
    public DbSet<MetricPoint> MetricPoints => Set<MetricPoint>();

    /// <summary>The log lines.</summary>
    public DbSet<LogRecordRow> LogRecords => Set<LogRecordRow>();

    /// <summary>The spans.</summary>
    public DbSet<SpanRow> Spans => Set<SpanRow>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MetricPoint>().HasIndex(point => new { point.ServerId, point.Name, point.AtUnixMs });
        modelBuilder.Entity<LogRecordRow>().HasIndex(line => new { line.ServerId, line.AtUnixMs });
        modelBuilder.Entity<SpanRow>().HasIndex(span => new { span.ServerId, span.StartUnixMs });
    }
}
