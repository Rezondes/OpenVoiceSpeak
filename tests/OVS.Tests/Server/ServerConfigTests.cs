using OVS.Server;
using OVS.Server.Exceptions;
using OVS.Server.Logging;
using OVS.Server.State;

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
        var map = vars.ToDictionary(value => value.Key, value => value.Value);
        map["OVS_DATA_DIR"] = dir;
        return key => map.GetValueOrDefault(key);
    }

    void WriteFile(string json)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, ServerConfig.FileName), json);
    }

    [Fact]
    public void Load_NoEnvNoFile_UsesDefaults()
    {
        var config = ServerConfig.Load(Env());
        Assert.Equal(7000, config.Port);
        Assert.Equal(50, config.MaxUsers);
        Assert.Equal("OpenVoiceSpeak Server", config.ServerName);
        Assert.Equal("", config.Password);
        Assert.Equal(Path.GetFullPath(dir), config.DataDir);
    }

    [Fact]
    public void Load_DefaultDataDir_IsData()
    {
        var config = ServerConfig.Load(_ => null);
        Assert.Equal(Path.GetFullPath("data"), config.DataDir);
    }

    [Fact]
    public void Load_FileValues_AreUsed()
    {
        WriteFile("""{"port":7100,"maxUsers":10,"serverName":"Datei"}""");
        var config = ServerConfig.Load(Env());
        Assert.Equal(7100, config.Port);
        Assert.Equal(10, config.MaxUsers);
        Assert.Equal("Datei", config.ServerName);
    }

    [Fact]
    public void Load_EnvAndFile_EnvWins()
    {
        WriteFile("""{"port":7100}""");
        var config = ServerConfig.Load(Env(("OVS_PORT", "7200"), ("OVS_PASSWORD", "geheim")));
        Assert.Equal(7200, config.Port);
        Assert.Equal("geheim", config.Password);
    }

    [Theory]
    [InlineData("OVS_PORT", "0")]
    [InlineData("OVS_PORT", "70000")]
    [InlineData("OVS_PORT", "abc")]
    [InlineData("OVS_MAX_USERS", "0")]
    [InlineData("OVS_SERVER_NAME", "   ")]
    public void Load_InvalidValue_ThrowsNamingVariable(string key, string value)
    {
        var exception = Assert.Throws<ConfigException>(() => ServerConfig.Load(Env((key, value))));
        Assert.Contains(key, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("abc")]
    public void Load_InvalidLogDays_Throws(string value)
    {
        var exception = Assert.Throws<ConfigException>(() => ServerConfig.Load(Env(("OVS_LOG_DAYS", value))));
        Assert.Contains("OVS_LOG_DAYS", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_LogDays_DefaultAndOverride()
    {
        Assert.Equal(30, ServerConfig.Load(Env()).LogDays);
        Assert.Equal(0, ServerConfig.Load(Env(("OVS_LOG_DAYS", "0"))).LogDays);
    }

    [Fact]
    public void Load_NoAutoRestartSet_OffWithTimeAt4am()
    {
        Assert.Null(ServerConfig.Load(Env(("OVS_AUTO_RESTART_TIME", "03:30:00"))).AutoRestartAt);
        Assert.Equal(new TimeOnly(4, 0, 0), ServerConfig.Load(Env(("OVS_AUTO_RESTART", "an"))).AutoRestartAt);
        Assert.Equal(new TimeOnly(3, 30, 15),
            ServerConfig.Load(Env(("OVS_AUTO_RESTART", "true"), ("OVS_AUTO_RESTART_TIME", "03:30:15"))).AutoRestartAt);
        Assert.Null(ServerConfig.Load(Env(("OVS_AUTO_RESTART", "aus"))).AutoRestartAt);
    }

    [Fact]
    public void Load_AutoRestartAndLogRotationInFileAndEnv_EnvWins()
    {
        WriteFile("""{"autoRestart":true,"autoRestartTime":"05:15:00","logRotateDaily":false}""");
        var config = ServerConfig.Load(Env());
        Assert.Equal(new TimeOnly(5, 15, 0), config.AutoRestartAt);
        Assert.False(config.LogRotateDaily);
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
        var exception = Assert.Throws<ConfigException>(() => ServerConfig.Load(Env((key, value))));
        Assert.Contains(key, exception.Message, StringComparison.Ordinal);
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
        var exception = Assert.Throws<ConfigException>(() => ServerConfig.Load(Env()));
        Assert.Contains(ServerConfig.FileName, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_DataDirIsAFile_ThrowsNamingVariable()
    {
        File.WriteAllText(dir, "kein Verzeichnis");
        try
        {
            var exception = Assert.Throws<ConfigException>(() => ServerConfig.Load(Env()));
            Assert.Contains("OVS_DATA_DIR", exception.Message, StringComparison.Ordinal);
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
        var exception = Assert.Throws<ConfigException>(() => ServerConfig.Load(Env()));
        Assert.Contains(ServerConfig.FileName, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Package 69: after the first start the values live in server-data.json; a differing variable is only noted.</summary>
    [Fact]
    public void Start_EnvDiffersFromStored_HintInLog()
    {
        var lines = new List<string>();
        using var logs = new ServerLogWriter(dir, 0, TimeProvider.System, lines.Add);
        var first = ServerConfig.Load(Env(("OVS_MAX_USERS", "20"), ("OVS_LOG_DAYS", "7")));
        _ = new ServerState(first, TimeProvider.System, logs, ServerStores.Open(first, TimeProvider.System));
        Assert.DoesNotContain(lines, line => line.Contains("OVS_", StringComparison.Ordinal));

        WriteFile("""{"autoRestart":true}""");
        var config = ServerConfig.Load(Env(("OVS_MAX_USERS", "99"), ("OVS_LOG_DAYS", "7")));
        _ = new ServerState(config, TimeProvider.System, logs, ServerStores.Open(config, TimeProvider.System));
        Assert.Contains(lines, line => line.Contains("OVS_MAX_USERS", StringComparison.Ordinal) && line.Contains("99", StringComparison.Ordinal) && line.Contains("20", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("OVS_AUTO_RESTART", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("OVS_LOG_DAYS", StringComparison.Ordinal)); // same as stored
        Assert.DoesNotContain(lines, line => line.Contains("OVS_LOG_ROTATE_DAILY", StringComparison.Ordinal)); // not set at all
    }
}
