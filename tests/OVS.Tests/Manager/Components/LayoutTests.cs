using OVS.Tests.TestSupport;

namespace OVS.Tests.Manager.Components;

/// <summary>Package M2: the layout's theme, light or dark or the system's, kept per browser.</summary>
public sealed class LayoutTests
{
    /// <summary>AC3: the system decides until a pick; the pick is stored and applied on every page.</summary>
    [Fact]
    public async Task Theme_Picked_StoredAndApplied()
    {
        await using var manager = new ManagerFactory();
        using var client = manager.CreateClient();
        Assert.DoesNotContain("data-theme=", await client.GetStringAsync(new Uri("/", UriKind.Relative)), StringComparison.Ordinal);

        Assert.Contains("data-theme=\"dark\"", await client.GetStringAsync(new Uri("/theme?value=dark&returnUrl=/", UriKind.Relative)), StringComparison.Ordinal);
        Assert.Contains("data-theme=\"dark\"", await client.GetStringAsync(new Uri("/", UriKind.Relative)), StringComparison.Ordinal);
        Assert.Contains("data-theme=\"light\"", await client.GetStringAsync(new Uri("/theme?value=light&returnUrl=/", UriKind.Relative)), StringComparison.Ordinal);
        Assert.DoesNotContain("data-theme=", await client.GetStringAsync(new Uri("/theme?value=system&returnUrl=/", UriKind.Relative)), StringComparison.Ordinal);
    }

    /// <summary>The manager is an alpha: every page says so in its title and beside the name.</summary>
    [Fact]
    public async Task Page_Rendered_MarkedAlpha()
    {
        await using var manager = new ManagerFactory();
        using var client = manager.CreateClient();

        var page = await client.GetStringAsync(new Uri("/login", UriKind.Relative));

        Assert.Contains("<title>OpenVoiceSpeak Verwaltung (Alpha)</title>", page, StringComparison.Ordinal);
        Assert.Contains("class=\"stage-badge\"", page, StringComparison.Ordinal);
    }
}
