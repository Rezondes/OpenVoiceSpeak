using OVS.Server.Data;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server.Data;

public sealed class UploadFolderTests() : TempDirectoryTest("ovs-uploads-", quiet: false)
{
    const string Id = "0123456789abcdef0123456789abcdef";

    string Folder => Path.Combine(dir, BackupStore.FolderName);

    [Theory]
    [InlineData(Id, true)]
    [InlineData("0123456789ABCDEF0123456789ABCDEF", false)]
    [InlineData("0123456789abcdef0123456789abcde", false)]
    [InlineData("0123456789abcdef0123456789abcdef0", false)]
    [InlineData("../../0123456789abcdef0123456789", false)]
    [InlineData("..\\0123456789abcdef0123456789abc", false)]
    [InlineData("0123456789abcdef0123456789abcdeg", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidId_Text_OnlyThirtyTwoLowercaseHexDigits(string? id, bool valid) =>
        Assert.Equal(valid, UploadFolder.IsValidId(id));

    [Fact]
    public void PreparePath_NoFolderYet_FolderCreatedAndHiddenFileNamed()
    {
        var path = new UploadFolder(Folder).PreparePath(Id);

        Assert.Equal(Path.Combine(Folder, ".upload-" + Id), path);
        Assert.True(Directory.Exists(Folder));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void RemoveLeftovers_NoFolder_NothingCreated()
    {
        new UploadFolder(Folder).RemoveLeftovers();

        Assert.False(Directory.Exists(Folder));
    }

    [Fact]
    public void RemoveLeftovers_Uploads_Removed()
    {
        Directory.CreateDirectory(Folder);
        foreach (var name in new[] { ".upload-" + Id, ".upload-x" })
            File.WriteAllText(Path.Combine(Folder, name), "x");

        new UploadFolder(Folder).RemoveLeftovers();

        Assert.Empty(Directory.GetFiles(Folder));
    }

    [Fact]
    public void RemoveLeftovers_OtherFiles_Kept()
    {
        var kept = new[] { "behalten.ovsbackup", "2026-01-01_12-00-00.ovsbackup.tmp", "upload-" + Id, "hochgeladen_2026-01-01_12-00-00.ovsbackup" };
        Directory.CreateDirectory(Folder);
        foreach (var name in kept) File.WriteAllText(Path.Combine(Folder, name), "x");
        var nested = Directory.CreateDirectory(Path.Combine(Folder, "unter")).FullName;
        File.WriteAllText(Path.Combine(nested, ".upload-" + Id), "x");

        new UploadFolder(Folder).RemoveLeftovers();

        Assert.Equal(kept.Order(StringComparer.Ordinal), Directory.GetFiles(Folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.True(File.Exists(Path.Combine(nested, ".upload-" + Id)));
    }
}
