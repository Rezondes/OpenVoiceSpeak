namespace OVS.Tests.TestSupport;

/// <summary>Package 171: one entry of a LevelDB fixture (LevelDbFiles); a null value is a deletion.</summary>
/// <param name="Key">The key as Latin-1 text.</param>
/// <param name="Value">The value, null for a deletion.</param>
/// <param name="Sequence">The write's sequence number; the higher one wins.</param>
sealed record LevelDbEntry(string Key, byte[]? Value, ulong Sequence);
