using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using OVS.Manager.Accounts;

namespace OVS.Manager.Data;

/// <summary>
/// Package M1: the manager's SQLite database in <see cref="ManagerOptions.DatabasePath"/>, migrated at every start.
/// Package M3: with the accounts and roles of ASP.NET Core Identity.
/// </summary>
internal sealed class ManagerDbContext(DbContextOptions<ManagerDbContext> options) : IdentityDbContext<ManagerUser>(options);
