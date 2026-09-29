using OVS.Server;
using OVS.Server.Logging;

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

    [Theory]
    [InlineData("-1")]
    [InlineData("abc")]
    public void Load_InvalidLogDays_Throws(string value)
    {
        var e = Assert.Throws<ConfigException>(() => ServerConfig.Load(Env(("OVS_LOG_DAYS", value))));
        Assert.Contains("OVS_LOG_DAYS", e.Message);
    }

    [Fact]
    public void Load_LogDays_DefaultAndOverride()
    {
        Assert.Equal(30, ServerConfig.Load(Env()).LogDays);
        Assert.Equal(0, ServerConfig.Load(Env(("OVS_LOG_DAYS", "0"))).LogDays);
    }

    [Fact]
    public void Load_AutoRestart_OffByDefault_TimeDefaultsTo4am()
    {
        Assert.Null(ServerConfig.Load(Env(("OVS_AUTO_RESTART_TIME", "03:30:00"))).AutoRestartAt);
        Assert.Equal(new TimeOnly(4, 0, 0), ServerConfig.Load(Env(("OVS_AUTO_RESTART", "an"))).AutoRestartAt);
        Assert.Equal(new TimeOnly(3, 30, 15),
            ServerConfig.Load(Env(("OVS_AUTO_RESTART", "true"), ("OVS_AUTO_RESTART_TIME", "03:30:15"))).AutoRestartAt);
        Assert.Null(ServerConfig.Load(Env(("OVS_AUTO_RESTART", "aus"))).AutoRestartAt);
    }

    [Fact]
    public void Load_AutoRestartAndLogRotation_FromFile_EnvWins()
    {
        WriteFile("""{"autoRestart":true,"autoRestartTime":"05:15:00","logRotateDaily":false}""");
        var cfg = ServerConfig.Load(Env());
        Assert.Equal(new TimeOnly(5, 15, 0), cfg.AutoRestartAt);
        Assert.False(cfg.LogRotateDaily);
        Assert.True(ServerConfig.Load(Env(("OVS_LOG_ROTATE_DAILY", "1"))).LogRotateDaily);
    }

    [Fact]
    public void Load_LogRotateDaily_OnByDefault() => Assert.True(ServerConfig.Load(Env()).LogRotateDaily);

    [Theory]
    [InlineData("OVS_AUTO_RESTART", "ja")]
    [InlineData("OVS_AUTO_RESTART_TIME", "25:00:00")]
    [InlineData("OVS_AUTO_RESTART_TIME", "4:00")]
    [InlineData("OVS_LOG_ROTATE_DAILY", "vielleicht")]
    public void Load_InvalidRestartOrRotation_Throws(string key, string value)
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

    /// <summary>Package 69: after the first start the values live in server-data.json; a differing variable is only noted.</summary>
    [Fact]
    public void EnvDiffersFromStored_HintInLog()
    {
        var lines = new List<string>();
        var logs = new ServerLogs(dir, 0, TimeProvider.System, lines.Add);
        _ = new ServerState(ServerConfig.Load(Env(("OVS_MAX_USERS", "20"), ("OVS_LOG_DAYS", "7"))), TimeProvider.System, logs);
        Assert.DoesNotContain(lines, l => l.Contains("OVS_"));

        WriteFile("""{"autoRestart":true}""");
        var config = ServerConfig.Load(Env(("OVS_MAX_USERS", "99"), ("OVS_LOG_DAYS", "7")));
        _ = new ServerState(config, TimeProvider.System, logs);
        Assert.Contains(lines, l => l.Contains("OVS_MAX_USERS") && l.Contains("99") && l.Contains("20"));
        Assert.Contains(lines, l => l.Contains("OVS_AUTO_RESTART"));
        Assert.DoesNotContain(lines, l => l.Contains("OVS_LOG_DAYS")); // same as stored
        Assert.DoesNotContain(lines, l => l.Contains("OVS_LOG_ROTATE_DAILY")); // not set at all
    }
}
