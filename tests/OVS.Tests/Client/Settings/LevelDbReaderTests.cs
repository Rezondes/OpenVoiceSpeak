using System.Text;
using OVS.Client.Settings;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Settings;

/// <summary>Package 171: the LevelDB folder of an app is read only, newest value per key, damaged parts skipped.</summary>
public sealed class LevelDbReaderTests() : TempDirectoryTest("ovs-leveldb-", quiet: true)
{
    static byte[] Bytes(string text) => Encoding.ASCII.GetBytes(text);

    [Fact]
    public void Read_LogAndTable_ReturnsLatestValuePerKey()
    {
        LevelDbFiles.WriteTable(Path.Combine(dir, "000005.ldb"), 1, [new("a", Bytes("alt"), 1), new("b", Bytes("weg"), 2)]);
        LevelDbFiles.WriteLog(Path.Combine(dir, "000006.log"), [new("a", Bytes("neu"), 5), new("b", null, 6), new("c", Bytes("drei"), 7)]);
        File.WriteAllText(Path.Combine(dir, "LOCK"), ""); // not a data file

        var read = LevelDbReader.Read(dir);

        Assert.Equal(["a", "c"], read.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(("neu", "drei"), (Encoding.ASCII.GetString(read["a"]), Encoding.ASCII.GetString(read["c"])));
    }

    [Fact]
    public void Read_DamagedBlock_SkippedNotThrown()
    {
        LevelDbFiles.WriteTable(Path.Combine(dir, "000005.ldb"), 7, [new("kaputt", Bytes("x"), 1)]); // an unknown compression
        LevelDbFiles.WriteTable(Path.Combine(dir, "000006.ldb"), 0, [new("gut", Bytes("ja"), 2)]);
        File.WriteAllBytes(Path.Combine(dir, "000007.ldb"), [1, 2, 3]); // shorter than a footer
        LevelDbFiles.WriteLog(Path.Combine(dir, "000008.log"), [new("log", Bytes("da"), 3)]);
        var log = File.ReadAllBytes(Path.Combine(dir, "000008.log"));
        File.WriteAllBytes(Path.Combine(dir, "000009.log"), log[..^2]); // cut off while written

        var read = LevelDbReader.Read(dir);

        Assert.Equal(["gut", "log"], read.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Read_LockedFolder_ReadsFromACopy()
    {
        var path = Path.Combine(dir, "000006.log");
        LevelDbFiles.WriteLog(path, [new("a", Bytes("1"), 1)]);
        var before = File.GetLastWriteTimeUtc(path);
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)) // the app has it open for writing
            Assert.Equal("1", Encoding.ASCII.GetString(LevelDbReader.Read(dir)["a"]));
        Assert.Equal(before, File.GetLastWriteTimeUtc(path));
        Assert.Equal(["000006.log"], Directory.GetFiles(dir).Select(Path.GetFileName));
    }

    [Fact]
    public void Unsnappy_LiteralAndCopies_Decoded()
    {
        // "abc", then a copy of 6 bytes 3 back (overlapping itself), then a copy with a two-byte offset
        byte[] block = [12, 2 << 2, (byte)'a', (byte)'b', (byte)'c', 1 | (6 - 4) << 2, 3, 2 | (3 - 1) << 2, 9, 0];
        Assert.Equal("abcabcabcabc", Encoding.ASCII.GetString(LevelDbReader.Unsnappy(block)));
        Assert.Throws<InvalidDataException>(() => LevelDbReader.Unsnappy([4, 1, 5])); // a copy before anything was written
    }
}
