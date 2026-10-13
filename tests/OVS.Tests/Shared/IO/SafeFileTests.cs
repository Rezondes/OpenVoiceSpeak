using OVS.Shared.IO;
using OVS.Shared.Protocol;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Shared.IO;

public sealed class SafeFileTests() : TempDirectoryTest("ovs-safefile-", quiet: false)
{
    public override void Dispose()
    {
        foreach (var file in Directory.GetFiles(dir)) File.SetAttributes(file, FileAttributes.Normal);
        base.Dispose();
    }

    [Fact]
    public void DeleteQuietly_MissingOrLocked_NoThrow()
    {
        SafeFile.DeleteQuietly(Path.Combine(dir, "fehlt.txt"));
        SafeFile.DeleteQuietly(Path.Combine(dir, "kein-ordner", "fehlt.txt"));

        var readOnly = Path.Combine(dir, "schreibgeschuetzt.txt");
        File.WriteAllText(readOnly, "x");
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);
        SafeFile.DeleteQuietly(readOnly);

        var locked = Path.Combine(dir, "offen.txt");
        File.WriteAllText(locked, "x");
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            SafeFile.DeleteQuietly(locked);

        var plain = Path.Combine(dir, "weg.txt");
        File.WriteAllText(plain, "x");
        SafeFile.DeleteQuietly(plain);
        Assert.False(File.Exists(plain));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WriteAtomic_ReplacesAndLeavesNoTmp(bool flushToDisk)
    {
        var path = Path.Combine(dir, "daten.bin");
        File.WriteAllBytes(path, [1, 2, 3, 4, 5]);

        SafeFile.WriteAtomic(path, [9, 8], flushToDisk);
        Assert.Equal([9, 8], File.ReadAllBytes(path));
        SafeFile.WriteAtomic(path, stream => stream.Write([7]), flushToDisk);
        Assert.Equal([7], File.ReadAllBytes(path));
        Assert.Equal([path], Directory.GetFiles(dir));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WriteAtomic_Fails_KeepsOldFile(bool flushToDisk)
    {
        var path = Path.Combine(dir, "daten.bin");
        File.WriteAllBytes(path, [1, 2, 3]);

        Assert.Throws<InvalidOperationException>(() =>
            SafeFile.WriteAtomic(path, _ => throw new InvalidOperationException("Platte voll"), flushToDisk));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(path));

        File.Delete(path + ".tmp"); // like before, a failed write leaves its temporary file
        Directory.CreateDirectory(path + ".tmp"); // the temporary file cannot be written
        Assert.ThrowsAny<Exception>(() => SafeFile.WriteAtomic(path, [9], flushToDisk));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(path));
    }

    [Fact]
    public void ReadChunk_OutsideAndInside()
    {
        var path = Path.Combine(dir, "gross.bin");
        var content = new byte[ProtocolLimits.BackupChunkBytes + 10];
        Random.Shared.NextBytes(content);
        File.WriteAllBytes(path, content);

        Assert.Null(SafeFile.ReadChunk(path, -1));
        Assert.Null(SafeFile.ReadChunk(path, content.Length + 1));

        var (first, total) = SafeFile.ReadChunk(path, 0)!;
        Assert.Equal(content.Length, total);
        Assert.Equal(content[..ProtocolLimits.BackupChunkBytes], first);
        var (rest, _) = SafeFile.ReadChunk(path, ProtocolLimits.BackupChunkBytes)!;
        Assert.Equal(content[ProtocolLimits.BackupChunkBytes..], rest);
        Assert.Empty(SafeFile.ReadChunk(path, content.Length)!.Bytes);
    }
}
