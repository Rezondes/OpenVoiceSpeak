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

    /// <summary>The examples Package 149 promises.</summary>
    static readonly string[] Promised = ["grafana-lgtm", "aspire-dashboard", "jaeger", "seq"];

    static IEnumerable<string> Folders() => Directory.GetDirectories(Root).Where(folder => File.Exists(Path.Combine(folder, "docker-compose.yml")));

    /// <summary>Each service of a compose file with the lines of its block.</summary>
    static Dictionary<string, List<string>> Services(string folder)
    {
        var services = new Dictionary<string, List<string>>();
        List<string>? current = null;
        bool inServices = false;
        foreach (var line in File.ReadAllLines(Path.Combine(folder, "docker-compose.yml")))
        {
            if (line.TrimStart().StartsWith('#') || line.Trim().Length == 0) continue;
            if (!line.StartsWith(' ')) inServices = line == "services:";
            else if (inServices && ServiceLine().Match(line) is { Success: true } service) services[service.Groups[1].Value] = current = [];
            else if (inServices) current?.Add(line.Trim());
        }
        return services;
    }

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
            var endpoint = Assert.Single(server, line => line.StartsWith("OTEL_EXPORTER_OTLP_ENDPOINT:", StringComparison.Ordinal));
            var host = EndpointHost().Match(endpoint).Groups[1].Value;
            Assert.True(services.ContainsKey(host) && host != "ovs-server", $"{folder}: {endpoint} names no backend of the file");
            Assert.Contains(services[host], line => line.StartsWith("- \"", StringComparison.Ordinal)); // the backend's UI port is published
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
                Assert.True(PinnedTag().IsMatch(image), $"{folder} {name}: {image} has no fixed version");
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

    [GeneratedRegex(@"^  ([a-z0-9-]+):$")]
    private static partial Regex ServiceLine();

    [GeneratedRegex(@"""\w+://([a-z0-9-]+)[:/""]")]
    private static partial Regex EndpointHost();

    [GeneratedRegex(@":\d+(\.\d+)+$")]
    private static partial Regex PinnedTag();
}
