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
        Assert.Contains("ENV ASPNETCORE_HTTP_PORTS=6999", runtime);
        Assert.Contains("EXPOSE 6999/tcp", runtime);
        Assert.Contains("ENV OVS_MANAGER_DATA_DIR=/data", runtime);
        Assert.Contains("VOLUME /data", runtime);
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
        foreach (var (name, lines) in services)
        {
            Assert.Contains("- ovs-managed", lines);
            if (lines.Any(line => line.StartsWith("build:", StringComparison.Ordinal))) continue; // built from the checkout until M32 releases the image
            var image = Assert.Single(lines, line => line.StartsWith("image:", StringComparison.Ordinal))["image:".Length..].Trim();
            Assert.True(ComposeFile.IsPinned(image), $"{name}: {image} has no fixed version");
        }
    }
}
