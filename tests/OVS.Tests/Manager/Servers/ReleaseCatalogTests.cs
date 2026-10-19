using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OVS.Manager.Components.Layout;
using OVS.Manager.Servers;
using OVS.Shared;
using OVS.Shared.Protocol;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Manager.Servers;

/// <summary>Package M17 (MA5, A206): the releases from GitHub and what they offer the servers and the manager.</summary>
public sealed class ReleaseCatalogTests
{
    static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    static Dictionary<string, object> Release(string version, DateTimeOffset published, int? protocol) => new()
    {
        ["tag_name"] = "deploy-" + version.Replace(".", "", StringComparison.Ordinal)[..7], ["name"] = "OpenVoiceSpeak " + version,
        ["published_at"] = published, ["draft"] = false, ["body"] = protocol is { } number ? $"Protokoll: {number}\n\n- a change" : "- a change",
    };

    static ReleaseCatalog Catalog(FakeHttp github, BuildInfo? manager = null, TimeProvider? time = null) =>
        new(new HttpClient(github), manager ?? new BuildInfo(Now.AddDays(-30), "abcdef0123", IsCi: false), time ?? new ManualTimeProvider(Now), NullLogger<ReleaseCatalog>.Instance);

    /// <summary>AC1: a server on an older release of the manager's protocol is offered the newest one.</summary>
    [Fact]
    public async Task Newer_SameProtocol_Offered()
    {
        using var catalog = Catalog(new FakeHttp(_ => FakeHttp.Json(new[]
        {
            Release("121026.aa", Now.AddDays(-7), WireProtocol.Version), Release("151026.bb", Now.AddDays(-4), WireProtocol.Version),
        })));
        await catalog.RefreshAsync();

        Assert.Equal("151026.bb", catalog.UpdateFor("121026.aa")?.Version);
        Assert.Null(catalog.UpdateFor("151026.bb"));
    }

    /// <summary>AC2: a release of a newer protocol is never offered to a server, but the manager's banner shows it with the compose command.</summary>
    [Fact]
    public async Task NewerProtocol_NotOffered_ManagerBanner()
    {
        var manager = new BuildInfo(Now.AddDays(-10), "abcdef0123", IsCi: true);
        using var catalog = Catalog(new FakeHttp(_ => FakeHttp.Json(new[]
        {
            Release("091026.aa", Now.AddDays(-10), WireProtocol.Version), Release("181026.cc", Now.AddDays(-1), WireProtocol.Version + 1),
        })), manager);
        await catalog.RefreshAsync();

        Assert.Null(catalog.UpdateFor("091026.aa"));
        Assert.Equal("181026.cc", catalog.ManagerUpdate?.Version);
        await using var context = new BunitContext();
        context.Services.AddSingleton(catalog);
        context.Services.AddSingleton<TimeProvider>(new ManualTimeProvider(Now));
        var banner = context.Render<ManagerUpdateBanner>();
        Assert.Contains("Neue Version des Verwaltungstools verfügbar: 181026.cc", banner.Find("[data-manager-update]").TextContent, StringComparison.Ordinal);
        Assert.Contains("docker compose pull ovs-manager", banner.Markup, StringComparison.Ordinal);
    }

    /// <summary>AC3: the newest is the latest published, though its version sorts first as text.</summary>
    [Fact]
    public async Task Order_TextOrderDiffers_ByDate()
    {
        using var catalog = Catalog(new FakeHttp(_ => FakeHttp.Json(new[]
        {
            Release("311225.zz", Now.AddDays(-20), WireProtocol.Version), Release("010126.aa", Now.AddDays(-2), WireProtocol.Version),
        })));
        await catalog.RefreshAsync();

        Assert.Equal("010126.aa", catalog.UpdateFor("311225.zz")?.Version);
        Assert.Null(catalog.UpdateFor("010126.aa"));
    }

    /// <summary>AC4: without GitHub the last catalog stays, and the banner says how old it is; before any catalog it says the check failed.</summary>
    [Fact]
    public async Task Offline_LastCatalog_KeptWithAge()
    {
        bool online = true;
        var time = new ManualTimeProvider(Now);
        using var catalog = Catalog(new FakeHttp(_ => online
            ? FakeHttp.Json(new[] { Release("121026.aa", Now.AddDays(-7), WireProtocol.Version) })
            : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)), time: time);
        await catalog.RefreshAsync();
        online = false;
        time.Advance(TimeSpan.FromHours(3));
        await catalog.RefreshAsync();

        Assert.Equal("121026.aa", Assert.Single(catalog.Releases).Version);
        await using var context = new BunitContext();
        context.Services.AddSingleton(catalog);
        context.Services.AddSingleton<TimeProvider>(time);
        Assert.Equal("Versionsprüfung nicht möglich, Stand vor 3 Stunden.", context.Render<ManagerUpdateBanner>().Find("[data-catalog-state]").TextContent);

        using var never = Catalog(new FakeHttp(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)), time: time);
        await never.RefreshAsync();
        await using var empty = new BunitContext();
        empty.Services.AddSingleton(never);
        empty.Services.AddSingleton<TimeProvider>(time);
        Assert.Equal("Versionsprüfung nicht möglich.", empty.Render<ManagerUpdateBanner>().Find("[data-catalog-state]").TextContent);
    }
}
