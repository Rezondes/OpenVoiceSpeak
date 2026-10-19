using Microsoft.EntityFrameworkCore;

namespace OVS.Manager.Data;

/// <summary>Package M1: the manager's SQLite database in <see cref="ManagerOptions.DatabasePath"/>, migrated at every start.</summary>
internal sealed class ManagerDbContext(DbContextOptions<ManagerDbContext> options) : DbContext(options);
