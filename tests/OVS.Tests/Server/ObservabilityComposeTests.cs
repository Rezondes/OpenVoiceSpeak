using System.Text.RegularExpressions;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server;

/// <summary>
/// Package 149 (A159): the compose examples under deploy/observability start ovs-server with one backend each, export to
/// a service of the same file and pin every image they do not build. Read line by line: the files are plain and small.
/// </summary>
public sealed partial class ObservabilityComposeTests
{
    static readonly string Root = Path.Combine(RepoPaths.Root, "deploy", "observability");

    /// <summary>The examples Packages 149 and 150 promise.</summary>
    static readonly string[] Promised = ["grafana-lgtm", "aspire-dashboard", "jaeger", "seq", "elastic", "opensearch", "netdata"];

    static IEnumerable<string> Folders() => Directory.GetDirectories(Root).Where(folder => File.Exists(Path.Combine(folder, "docker-compose.yml")));

    static Dictionary<string, List<string>> Services(string folder) => ComposeFile.ReadServices(Path.Combine(folder, "docker-compose.yml"));

    [Fact]
    public void Compose_Endpoint_PointsAtOwnService()
    {
        Assert.Subset(Folders().Select(Path.GetFileName).ToHashSet(), Promised.ToHashSet<string?>());
        foreach (var folder in Folders())
        {
            var services = Services(folder);
            var server = services["ovs-server"];
            Assert.Contains("- \"7000:7000/tcp\"", server);
            Assert.Contains("- \"7000:7000/udp\"", server);
            Assert.Single(server, line => line.StartsWith("OTEL_EXPORTER_OTLP_ENDPOINT:", StringComparison.Ordinal)); // switches it on
            // Package 150: the base endpoint and any per-signal one (Data Prepper takes each signal on its own port)
            foreach (var endpoint in server.Where(line => EndpointVariable().IsMatch(line)))
            {
                var host = EndpointHost().Match(endpoint).Groups[1].Value;
                Assert.True(services.ContainsKey(host) && host != "ovs-server", $"{folder}: {endpoint} names no backend of the file");
            }
            Assert.Contains(services, service => service.Key != "ovs-server" && service.Value.Any(line => line.StartsWith("- \"", StringComparison.Ordinal))); // a UI port is published
        }
    }

    /// <summary>Package 150: every config file a service mounts from its folder is there.</summary>
    [Fact]
    public void Compose_MountedConfigFiles_Exist()
    {
        foreach (var folder in Folders())
            foreach (var (name, lines) in Services(folder))
                foreach (var mount in lines.Where(line => line.StartsWith("- ./", StringComparison.Ordinal)))
                {
                    var source = mount[2..].Split(':')[0];
                    Assert.True(File.Exists(Path.Combine(folder, source)), $"{folder} {name}: {source} is missing");
                }
    }

    [Fact]
    public void Compose_EveryImage_Pinned()
    {
        foreach (var folder in Folders())
            foreach (var (name, lines) in Services(folder))
            {
                if (lines.Any(line => line.StartsWith("build:", StringComparison.Ordinal))) continue; // built from the checkout
                var image = Assert.Single(lines, line => line.StartsWith("image:", StringComparison.Ordinal))["image:".Length..].Trim();
                Assert.True(ComposeFile.IsPinned(image), $"{folder} {name}: {image} has no fixed version");
            }
    }

    [Fact]
    public void Readme_EveryBackend_Named()
    {
        var readme = File.ReadAllLines(Path.Combine(Root, "README.md"));
        foreach (var folder in Folders().Select(Path.GetFileName))
            Assert.Contains(readme, line => line.StartsWith($"| `{folder}` |", StringComparison.Ordinal) && line.Contains("http://localhost:", StringComparison.Ordinal));
        Assert.Contains(readme, line => line.Contains("docker compose up -d --build", StringComparison.Ordinal));
        Assert.Contains(readme, line => line.Contains("docker compose down -v", StringComparison.Ordinal));
    }

    [GeneratedRegex(@"""\w+://([a-z0-9-]+)[:/""]")]
    private static partial Regex EndpointHost();

    [GeneratedRegex(@"^OTEL_EXPORTER_OTLP_(TRACES_|METRICS_|LOGS_)?ENDPOINT:")]
    private static partial Regex EndpointVariable();
}
