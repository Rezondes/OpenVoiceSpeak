using OVS.Server;

namespace OVS.Tests.Server;

public sealed class ServerConfigTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "ovs-cfg-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    Func<string, string?> Env(params (string Key, string Value)[] vars)
    {
        var map = vars.ToDictionary(v => v.Key, v => v.Value);
        map["OVS_DATA_DIR"] = dir;
        return k => map.GetValueOrDefault(k);
    }

    void WriteFile(string json)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, ServerConfig.FileName), json);
    }

    [Fact]
    public void Load_NoEnvNoFile_UsesDefaults()
    {
        var cfg = ServerConfig.Load(Env());
        Assert.Equal(7000, cfg.Port);
        Assert.Equal(50, cfg.MaxUsers);
        Assert.Equal("OpenVoiceSpeak Server", cfg.ServerName);
        Assert.Equal("", cfg.Password);
        Assert.Equal(Path.GetFullPath(dir), cfg.DataDir);
    }

    [Fact]
    public void Load_DefaultDataDir_IsData()
    {
        var cfg = ServerConfig.Load(_ => null);
        Assert.Equal(Path.GetFullPath("data"), cfg.DataDir);
    }

    [Fact]
    public void Load_FileValues_AreUsed()
    {
        WriteFile("""{"port":7100,"maxUsers":10,"serverName":"Datei"}""");
        var cfg = ServerConfig.Load(Env());
        Assert.Equal(7100, cfg.Port);
        Assert.Equal(10, cfg.MaxUsers);
        Assert.Equal("Datei", cfg.ServerName);
    }

    [Fact]
    public void Load_EnvOverridesFile()
    {
        WriteFile("""{"port":7100}""");
        var cfg = ServerConfig.Load(Env(("OVS_PORT", "7200"), ("OVS_PASSWORD", "geheim")));
        Assert.Equal(7200, cfg.Port);
        Assert.Equal("geheim", cfg.Password);
    }

    [Theory]
    [InlineData("OVS_PORT", "0")]
    [InlineData("OVS_PORT", "70000")]
    [InlineData("OVS_PORT", "abc")]
    [InlineData("OVS_MAX_USERS", "0")]
    [InlineData("OVS_SERVER_NAME", "   ")]
    public void Load_InvalidValue_ThrowsNamingVariable(string key, string value)
    {
        var e = Assert.Throws<ConfigException>(() => ServerConfig.Load(Env((key, value))));
        Assert.Contains(key, e.Message);
    }

    [Fact]
    public void Load_MissingDataDir_IsCreated()
    {
        ServerConfig.Load(Env());
        Assert.True(Directory.Exists(dir));
    }

    [Fact]
    public void Load_ConfigFileUnreadable_ThrowsNamingFile()
    {
        WriteFile("""{"port":7100}""");
        using var locked = new FileStream(Path.Combine(dir, ServerConfig.FileName), FileMode.Open, FileAccess.Read, FileShare.None);
        var e = Assert.Throws<ConfigException>(() => ServerConfig.Load(Env()));
        Assert.Contains(ServerConfig.FileName, e.Message);
    }

    [Fact]
    public void Load_DataDirIsAFile_ThrowsNamingVariable()
    {
        File.WriteAllText(dir, "kein Verzeichnis");
        try
        {
            var e = Assert.Throws<ConfigException>(() => ServerConfig.Load(Env()));
            Assert.Contains("OVS_DATA_DIR", e.Message);
        }
        finally
        {
            File.Delete(dir);
        }
    }

    [Fact]
    public void Load_CorruptConfigFile_ThrowsNamingFile()
    {
        WriteFile("{ kaputt");
        var e = Assert.Throws<ConfigException>(() => ServerConfig.Load(Env()));
        Assert.Contains(ServerConfig.FileName, e.Message);
    }
}
