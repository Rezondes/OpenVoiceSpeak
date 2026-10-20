using OVS.Tests.TestSupport;

namespace OVS.Tests.Manager;

/// <summary>Package M1: the manager's image and its compose file, read as text like the observability examples (149).</summary>
public sealed class ManagerComposeTests
{
    static readonly string ComposePath = Path.Combine(RepoPaths.Root, "deploy", "manager", "docker-compose.yml");

    /// <summary>AC2: the runtime stage runs as the image's non-root user, listens on 6999 and keeps its data in /data.</summary>
    [Fact]
    public void Image_RuntimeStage_NonRootPort6999DataVolume()
    {
        var lines = File.ReadAllLines(Path.Combine(RepoPaths.Root, "Dockerfile.manager")).Select(line => line.Trim()).ToList();
        var runtime = lines.Skip(lines.FindLastIndex(line => line.StartsWith("FROM ", StringComparison.Ordinal))).ToList();
        Assert.Contains("USER $APP_UID", runtime);
        Assert.Contains("ENV ASPNETCORE_HTTP_PORTS=6999;4318", runtime); // Package M20: 4318 is OTLP, never published
        Assert.Contains("EXPOSE 6999/tcp", runtime);
        Assert.Contains("ENV OVS_MANAGER_DATA_DIR=/data", runtime);
        Assert.Contains("VOLUME /data", runtime);
    }

    /// <summary>
    /// Package M6 (AC1, D8): the proxy gets the socket read-only, publishes nothing, lives in an internal network and
    /// passes on exactly the calls the manager makes: GET of ping, container list, inspect, logs, stats, image inspect and system df (M25);
    /// POST of container create, start, stop, restart, image pull, volume create and network connect; DELETE of a
    /// container or a volume.
    /// </summary>
    [Fact]
    public void SocketProxy_Compose_ReadOnlyNoPortAllowList()
    {
        var proxy = ComposeFile.ReadServices(ComposePath)["socket-proxy"];
        Assert.Contains("- /var/run/docker.sock:/var/run/docker.sock:ro", proxy);
        Assert.Contains("user: \"65534:${DOCKER_SOCKET_GID:-999}\"", proxy); // the socket's group differs by host (Docker Desktop: 0)
        Assert.DoesNotContain(proxy, line => line.StartsWith("ports:", StringComparison.Ordinal));
        Assert.Equal(["- docker-proxy"], proxy.SkipWhile(line => line != "networks:").Skip(1).TakeWhile(line => line.StartsWith('-')));
        Assert.Contains("internal: true", ComposeFile.ReadBlock(ComposePath, "networks"));
        var allowed = proxy.Where(line => line.StartsWith("- \"-allow", StringComparison.Ordinal) && !line.StartsWith("- \"-allowfrom", StringComparison.Ordinal)).ToList();
        Assert.Equal(
        [
            @"- ""-allowGET=^(/v1\\.[0-9]+)?/(_ping|containers/json|containers/[a-zA-Z0-9_.-]+/(json|logs|stats)|images/json|images/.+/json|system/df)$""", // Package M25: system/df for the volume sizes; images/json for a local build's default version
            @"- ""-allowPOST=^(/v1\\.[0-9]+)?/(containers/create|containers/[a-zA-Z0-9_.-]+/(start|stop|restart)|images/create|volumes/create|networks/[a-zA-Z0-9_.-]+/connect)$""",
            @"- ""-allowDELETE=^(/v1\\.[0-9]+)?/(containers|volumes)/[a-zA-Z0-9_.-]+$""",
        ], allowed);
    }

    /// <summary>Package M32 (AC1): Caddy serves the manager on 443 for the domain; the manager publishes nothing then, trusts only Caddy and takes the domain as its base URL.</summary>
    [Fact]
    public void Caddy_Https443_Pinned()
    {
        var caddyCompose = Path.Combine(RepoPaths.Root, "deploy", "manager", "caddy", "docker-compose.yml");
        var services = ComposeFile.ReadServices(caddyCompose);
        var caddy = services["caddy"];
        Assert.True(ComposeFile.IsPinned(Assert.Single(caddy, line => line.StartsWith("image:", StringComparison.Ordinal))["image:".Length..].Trim()));
        Assert.Contains("- \"443:443\"", caddy);
        Assert.Contains("- ./caddy/Caddyfile:/etc/caddy/Caddyfile:ro", caddy);
        Assert.Contains("ipv4_address: 10.255.250.2", caddy);
        var manager = services["ovs-manager"];
        Assert.Contains(manager, line => line.StartsWith("ports: !reset []", StringComparison.Ordinal));
        Assert.Contains(manager, line => line.StartsWith("OVS_MANAGER_BASE_URL: \"https://${OVS_MANAGER_DOMAIN", StringComparison.Ordinal));
        Assert.Contains(manager, line => line.StartsWith("OVS_MANAGER_TRUSTED_PROXIES: \"10.255.250.2\"", StringComparison.Ordinal));
        var caddyfile = File.ReadAllLines(Path.Combine(RepoPaths.Root, "deploy", "manager", "caddy", "Caddyfile")).Select(line => line.Trim()).ToList();
        Assert.Contains("{$OVS_MANAGER_DOMAIN} {", caddyfile);
        Assert.Contains("reverse_proxy ovs-manager:6999", caddyfile);
    }

    /// <summary>AC3: only 6999 is published, the network has its fixed name and every image it pulls a fixed version.</summary>
    [Fact]
    public void Compose_Services_OnlyPort6999NetworkNamedImagesPinned()
    {
        var services = ComposeFile.ReadServices(ComposePath);
        var published = services.Values.SelectMany(lines => lines).Where(line => line.StartsWith("- \"", StringComparison.Ordinal) && line.Contains(':', StringComparison.Ordinal)
            && !line.Contains('/', StringComparison.Ordinal)).ToList();
        Assert.Equal(["- \"6999:6999\""], published);
        Assert.Contains("name: ovs-managed", ComposeFile.ReadBlock(ComposePath, "networks"));
        Assert.Contains("- ovs-managed", services["ovs-manager"]);
        foreach (var (name, lines) in services)
        {
            if (lines.Any(line => line.StartsWith("build:", StringComparison.Ordinal))) continue; // built from the checkout until M32 releases the image
            var image = Assert.Single(lines, line => line.StartsWith("image:", StringComparison.Ordinal))["image:".Length..].Trim();
            Assert.True(ComposeFile.IsPinned(image), $"{name}: {image} has no fixed version");
        }
    }
}
