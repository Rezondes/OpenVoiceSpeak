using OVS.Server;
using OVS.Server.Data;

namespace OVS.Tests.Server;

public sealed class DataStoreTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("ovs-data-").FullName;
    string FilePath => Path.Combine(dir, DataStore.FileName);
    ServerConfig Config => new(7000, dir, 50, "Mein Server", "pw");

    public void Dispose() => Directory.Delete(dir, true);

    [Fact]
    public void Load_MissingFile_CreatesDefaults()
    {
        var data = new DataStore(FilePath).LoadOrCreate(() => ServerData.CreateDefault(Config));

        var lobby = Assert.Single(data.Channels);
        Assert.Equal("Lobby", lobby.Name);
        Assert.Equal(lobby.Id, data.DefaultChannelId);
        Assert.Equal(["Gast", "Moderator", "Admin"], data.Groups.Select(g => g.Name));
        Assert.Equal("Mein Server", data.Settings.Name);
        Assert.True(data.Settings.CheckPassword("pw"));
        Assert.False(data.Settings.CheckPassword("nein"));
        Assert.True(File.Exists(FilePath));
    }

    [Fact]
    public void Save_ThenLoad_RoundTrips_NoTempLeft()
    {
        var store = new DataStore(FilePath);
        var data = store.LoadOrCreate(() => ServerData.CreateDefault(Config));
        data.Channels.Add(new ChannelRecord { Id = Guid.NewGuid(), Name = "Raid", Order = 1 });
        data.Links.Add(ChannelLink.Of(data.Channels[0].Id, data.Channels[1].Id));
        data.Users.Add(new UserRecord { Fingerprint = "ab", LastNickname = "x", GroupIds = [data.Groups[0].Id], FirstSeen = DateTimeOffset.UnixEpoch });
        store.Save(data);

        var loaded = new DataStore(FilePath).LoadOrCreate(() => throw new InvalidOperationException());
        Assert.Equal(["Lobby", "Raid"], loaded.Channels.Select(c => c.Name));
        Assert.Equal(data.Links, loaded.Links);
        Assert.Equal(data.Groups, loaded.Groups);
        Assert.Equal("ab", Assert.Single(loaded.Users).Fingerprint);
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void Load_CorruptFile_ThrowsAndLeavesFileUntouched()
    {
        File.WriteAllText(FilePath, "{ kaputt");
        var before = File.ReadAllBytes(FilePath);

        Assert.Throws<InvalidDataException>(() => new DataStore(FilePath).LoadOrCreate(() => ServerData.CreateDefault(Config)));
        Assert.Equal(before, File.ReadAllBytes(FilePath));
    }
}
