namespace OVS.Manager.Data;

/// <summary>Package M33 (AC1): what a manager backup says about itself (manifest.json).</summary>
/// <param name="Format">The archive format, <see cref="CurrentFormat"/>.</param>
/// <param name="ManagerVersion">The version of the manager that made it.</param>
/// <param name="CreatedAt">When it was made.</param>
/// <param name="Migration">The newest migration of its database; a manager that does not know it is older (AC3).</param>
/// <param name="ServerIds">The ids of the servers in it.</param>
internal sealed record ManagerBackupManifest(int Format, string ManagerVersion, DateTimeOffset CreatedAt, string Migration, IReadOnlyList<string> ServerIds)
{
    /// <summary>The format this manager writes and reads.</summary>
    public const int CurrentFormat = 1;

    /// <summary>The manifest's entry.</summary>
    public const string Entry = "manifest.json";

    /// <summary>The database's entry.</summary>
    public const string DatabaseEntry = "manager.db";

    /// <summary>The folder of the Data Protection keys in the archive.</summary>
    public const string KeysFolder = "keys/";
}
