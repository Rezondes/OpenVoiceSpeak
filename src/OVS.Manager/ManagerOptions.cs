using Microsoft.Extensions.Configuration;

namespace OVS.Manager;

/// <summary>Package M1: the manager's settings from the environment (MA1); the port is the image's, set in <c>Dockerfile.manager</c>.</summary>
/// <param name="DataDir">The folder of the database and the keys, <c>OVS_MANAGER_DATA_DIR</c>, <c>/data</c> by default.</param>
internal sealed record ManagerOptions(string DataDir)
{
    /// <summary>The environment variable of <see cref="DataDir"/>.</summary>
    public const string DataDirVariable = "OVS_MANAGER_DATA_DIR";

    /// <summary>The database file in <see cref="DataDir"/>.</summary>
    public string DatabasePath => Path.Combine(DataDir, "manager.db");

    /// <summary>The settings in <paramref name="configuration"/>, the data folder as a full path.</summary>
    public static ManagerOptions Read(IConfiguration configuration) =>
        new(Path.GetFullPath(configuration[DataDirVariable] is { Length: > 0 } folder ? folder : "/data"));
}
