using OVS.Server;

try
{
    var config = ServerConfig.Load(Environment.GetEnvironmentVariable);
    Console.WriteLine($"Konfiguration geladen: Port {config.Port}, Daten in {config.DataDir}");
    return 0;
}
catch (ConfigException e)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}
