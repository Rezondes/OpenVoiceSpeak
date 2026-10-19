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

    /// <summary>Package M21: the minute and hour values.</summary>
    public DbSet<MetricRollup> MetricRollups => Set<MetricRollup>();

    /// <summary>Package M21: the last cumulative value of each counter series.</summary>
    public DbSet<CounterState> CounterStates => Set<CounterState>();

    /// <summary>Package M21: the daily statistics, never deleted.</summary>
    public DbSet<DailyStats> DailyStats => Set<DailyStats>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MetricPoint>().HasIndex(point => new { point.ServerId, point.Name, point.AtUnixMs });
        modelBuilder.Entity<LogRecordRow>().HasIndex(line => new { line.ServerId, line.AtUnixMs });
        modelBuilder.Entity<SpanRow>().HasIndex(span => new { span.ServerId, span.StartUnixMs });
        modelBuilder.Entity<MetricRollup>().HasIndex(row => new { row.ServerId, row.Level, row.PeriodUnixMs });
        modelBuilder.Entity<CounterState>().HasKey(state => new { state.ServerId, state.Name, state.Attributes });
        modelBuilder.Entity<DailyStats>().HasKey(day => new { day.ServerId, day.Day });
    }
}
