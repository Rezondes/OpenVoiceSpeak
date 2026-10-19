using System.Net;
using OVS.Manager;
using OVS.Manager.Accounts;
using OVS.Manager.Docker;
using OVS.Manager.Exceptions;
using OVS.Tests.Manager.Accounts;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Manager.Docker;

/// <summary>Package M6 (D8): the manager's Docker adapter against an API on localhost; it touches only its own containers.</summary>
public sealed class DockerEngineTests
{
    const string Containers = """
        [
          { "Id": "m1", "Image": "ghcr.io/rezondes/openvoicespeak-server:191026.ab", "State": "running",
            "Labels": { "ovs.managed": "true", "ovs.server-id": "s1", "ovs.port": "7001", "ovs.version": "191026.ab" } },
          { "Id": "x", "Image": "nginx:1.29", "State": "running", "Labels": { "com.example": "other" } }
        ]
        """;

    static DockerEngine Engine(FakeDockerApi api) => new(new ManagerOptions("unused") { DockerHost = api.Host });

    /// <summary>AC2: Docker filters by the label, and the adapter checks it again.</summary>
    [Fact]
    public async Task List_Labels_OnlyManaged()
    {
        await using var api = new FakeDockerApi { ContainersJson = Containers };
        using var engine = Engine(api);
        var container = Assert.Single(await engine.ListAsync());
        Assert.Equal(new ManagedContainer("m1", "s1", 7001, "191026.ab", "running", "ghcr.io/rezondes/openvoicespeak-server:191026.ab"), container);
        Assert.Contains(api.Requests, request => request.Path.Contains("/containers/json", StringComparison.Ordinal)
            && WebUtility.UrlDecode(request.Path).Contains("ovs.managed=true", StringComparison.Ordinal));
    }

    /// <summary>AC2: a call for a container that is not the manager's never reaches Docker.</summary>
    [Fact]
    public async Task Call_ForeignContainer_RefusedBeforeDocker()
    {
        await using var api = new FakeDockerApi { ContainersJson = Containers };
        using var engine = Engine(api);
        await Assert.ThrowsAsync<ForeignContainerException>(() => engine.StartAsync("x"));
        await Assert.ThrowsAsync<ForeignContainerException>(() => engine.RemoveAsync("x"));
        Assert.DoesNotContain(api.Requests, request => request.Path.Contains("/containers/x", StringComparison.Ordinal));

        await engine.StartAsync("m1");
        Assert.Contains(api.Requests, request => request is { Method: "POST" } && request.Path.Contains("/containers/m1/start", StringComparison.Ordinal));
    }

    /// <summary>AC3: the labels, the volume ovs-&lt;id&gt;-data, the ports, the network and the restart policy.</summary>
    [Fact]
    public async Task Create_Spec_LabelsAndVolumeName()
    {
        await using var api = new FakeDockerApi();
        using var engine = Engine(api);
        var spec = new ContainerSpec("s7", "ghcr.io/rezondes/openvoicespeak-server", "191026.ab", 7007,
            new Dictionary<string, string> { ["OVS_PORT"] = "7007" }, "ovs-managed");
        Assert.Equal("created-1", await engine.CreateAsync(spec));

        var volume = Assert.Single(api.Requests, request => request.Path.Contains("/volumes/create", StringComparison.Ordinal)).Body;
        Assert.Contains("\"Name\":\"ovs-s7-data\"", volume, StringComparison.Ordinal);
        var create = Assert.Single(api.Requests, request => request.Path.Contains("/containers/create", StringComparison.Ordinal));
        Assert.Contains("name=ovs-s7", create.Path, StringComparison.Ordinal);
        foreach (var part in new[]
                 {
                     "\"ovs.managed\":\"true\"", "\"ovs.server-id\":\"s7\"", "\"ovs.port\":\"7007\"", "\"ovs.version\":\"191026.ab\"",
                     "\"Image\":\"ghcr.io/rezondes/openvoicespeak-server:191026.ab\"", "\"ovs-s7-data:/data\"", "\"OVS_PORT=7007\"",
                     "\"7007/tcp\"", "\"7007/udp\"", "\"HostPort\":\"7007\"", "\"NetworkMode\":\"ovs-managed\"", "\"unless-stopped\"",
                 })
            Assert.Contains(part, create.Body, StringComparison.Ordinal);
    }

    /// <summary>Package M8: inspect gives the state, exit code, restart count and start; a foreign container is refused.</summary>
    [Fact]
    public async Task Inspect_Labels_StateOrRefused()
    {
        await using var api = new FakeDockerApi
        {
            InspectJson = """
                { "Id": "m1", "RestartCount": 2, "State": { "Status": "exited", "ExitCode": 137, "StartedAt": "2026-10-19T10:00:00.5Z" },
                  "Config": { "Labels": { "ovs.managed": "true", "ovs.server-id": "s1" } } }
                """,
        };
        using var engine = Engine(api);
        Assert.Equal(new InspectedContainer("exited", 137, 2, new DateTimeOffset(2026, 10, 19, 10, 0, 0, 500, TimeSpan.Zero)), await engine.InspectAsync("m1"));

        api.InspectJson = """{ "Id": "x", "State": { "Status": "running", "StartedAt": "0001-01-01T00:00:00Z" }, "Config": { "Labels": {} } }""";
        await Assert.ThrowsAsync<ForeignContainerException>(() => engine.InspectAsync("x"));
    }

    /// <summary>AC4: without the proxy the health check says so with 503, the overview shows it, and the rest works.</summary>
    [Fact]
    public async Task Health_ProxyDown_Degraded()
    {
        await using var manager = new ManagerFactory((FirstAdmin.UserVariable, "chef"), (FirstAdmin.PasswordVariable, FirstAdminTests.Password),
            (ManagerOptions.DockerHostVariable, "tcp://127.0.0.1:9")) { RealDocker = true };
        using var client = manager.CreateClient();
        using var health = await client.GetAsync(new Uri("/healthz", UriKind.Relative));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, health.StatusCode);
        Assert.Contains("\"docker\":false", await health.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var home = await LoginTests.LoginAsync(client, "chef", FirstAdminTests.Password);
        Assert.Contains("Docker nicht erreichbar", await home.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
