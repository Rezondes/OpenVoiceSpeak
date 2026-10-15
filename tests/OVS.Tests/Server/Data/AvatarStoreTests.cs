using OVS.Server.Data;
using OVS.Shared.Protocol;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server.Data;

/// <summary>Package 168 (A170): the pictures on disk, named by the fingerprint and nothing else.</summary>
public sealed class AvatarStoreTests() : TempDirectoryTest("ovs-avatars-", quiet: false)
{
    static readonly string Fingerprint = new('a', 64);

    [Theory]
    [InlineData("../../evil")]
    [InlineData("..\\evil")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // capitals are no fingerprint the server makes
    [InlineData("aaaa")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    [InlineData("")]
    public void Name_AnyText_FromFingerprintOnly(string text)
    {
        Assert.Null(AvatarStore.FileNameFor(text));
        Assert.Throws<ArgumentException>(() => new AvatarStore(dir).Set(text, TestPng.Rgba()));
        Assert.Equal(Fingerprint + ".png", AvatarStore.FileNameFor(Fingerprint));
        Assert.False(Directory.Exists(Path.Combine(dir, AvatarStore.FolderName))); // nothing written anywhere
    }

    [Fact]
    public void Write_Picture_Atomic()
    {
        var store = new AvatarStore(dir);
        var first = TestPng.Rgba();
        var second = TestPng.Rgba(pixels: [.. TestPng.Raw(128, 4).Select(value => (byte)(value / 2))]);
        store.Set(Fingerprint, first);
        store.Set(Fingerprint, second);
        var folder = Path.Combine(dir, AvatarStore.FolderName);
        Assert.Equal([Fingerprint + ".png"], Directory.GetFiles(folder).Select(Path.GetFileName)); // no temporary file left
        Assert.Equal(second, File.ReadAllBytes(Path.Combine(folder, Fingerprint + ".png")));
        Assert.Equal(AvatarHash.Of(second), store.HashOf(Fingerprint));
        Assert.Equal(Convert.ToBase64String(second), store.Find(AvatarHash.Of(second)));
        Assert.Null(store.Find(AvatarHash.Of(first)));

        // loaded again: a file that fails the check or has no fingerprint as its name counts as none
        File.WriteAllBytes(Path.Combine(folder, new string('b', 64) + ".png"), [.. second, 0]);
        File.WriteAllBytes(Path.Combine(folder, "fremd.png"), second);
        var again = new AvatarStore(dir);
        Assert.Equal(AvatarHash.Of(second), again.HashOf(Fingerprint));
        Assert.Null(again.HashOf(new string('b', 64)));

        again.Set(Fingerprint, null);
        Assert.Null(again.HashOf(Fingerprint));
        Assert.False(File.Exists(Path.Combine(folder, Fingerprint + ".png")));
        again.Set(new string('c', 64), null); // nothing to remove is no error
    }
}
