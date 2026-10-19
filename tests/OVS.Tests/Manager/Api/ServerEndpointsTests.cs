using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OVS.Manager.Api;
using OVS.Manager.Audit;
using OVS.Manager.Data;
using OVS.Manager.Servers;
using OVS.Tests.Manager.Servers;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Manager.Api;

/// <summary>Package M31: API tokens and the REST API on the servers.</summary>
public sealed partial class ServerEndpointsTests
{
    static async Task<string> TokenAsync(ManagerFactory manager, string userId)
    {
        await using var scope = manager.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<ApiTokenStore>().CreateAsync(userId, "anna", "skript", 30))!;
    }

    static HttpClient Client(ManagerFactory manager, string? secret)
    {
        var client = manager.CreateClient(new() { AllowAutoRedirect = false });
        if (secret is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        return client;
    }

    static async Task<HttpStatusCode> PostAsync(HttpClient client, string path)
    {
        using var answer = await client.PostAsync(new Uri(path, UriKind.Relative), null);
        return answer.StatusCode;
    }

    static async Task<HttpStatusCode> GetAsync(HttpClient client, string path)
    {
        using var answer = await client.GetAsync(new Uri(path, UriKind.Relative));
        return answer.StatusCode;
    }

    /// <summary>Anna, a manager of "Raid" but not of "Lobby".</summary>
    static async Task<AnnaWithServers> AnnaAsync(ManagerFactory manager)
    {
        manager.CreateClient().Dispose();
        var raid = await StatusWatcherTests.CreateAsync(manager, "Raid");
        var lobby = await StatusWatcherTests.CreateAsync(manager, "Lobby");
        var anna = await ManagerSignIn.CreateAsync(manager, "anna");
        await using (var scope = manager.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ServerAssignmentList>().SetAsync(anna, raid.Id, true, "chef");
        await manager.Services.GetRequiredService<StatusWatcher>().RefreshAsync();
        return new AnnaWithServers(anna, raid.Id, lobby.Id);
    }

    /// <summary>AC1: the page shows a new secret once; only its hash is stored; a revoked token no longer works.</summary>
    [Fact]
    public async Task Token_Created_ShownOnceHashedRevoked()
    {
        await using var manager = StatusWatcherTests.WithAdmin();
        await AnnaAsync(manager);
        using var browser = await ManagerSignIn.LoginAsync(manager, "anna");

        using var created = await ManagerForm.SubmitAsync(browser, "/profile/api-tokens", "create-token", ("CreateModel.Name", "skript"), ("CreateModel.Days", "30"));
        var secret = Secret().Match(await created.Content.ReadAsStringAsync()).Value;
        Assert.NotEmpty(secret);
        Assert.DoesNotContain(secret, await browser.GetStringAsync(new Uri("/profile/api-tokens", UriKind.Relative)), StringComparison.Ordinal);
        ApiToken row;
        await using (var scope = manager.Services.CreateAsyncScope())
            row = await scope.ServiceProvider.GetRequiredService<ManagerDbContext>().ApiTokens.SingleAsync();
        Assert.Equal((ApiTokenStore.HashOf(secret), "skript"), (row.Hash, row.Name));
        using var api = Client(manager, secret);
        Assert.Equal(HttpStatusCode.OK, await GetAsync(api, "/api/servers"));

        (await ManagerForm.SubmitAsync(browser, "/profile/api-tokens", "revoke-token", ("RevokeModel.TokenId", row.Id))).Dispose();

        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(api, "/api/servers"));
    }

    /// <summary>AC2: the token sees and operates only its user's servers; without a token nothing.</summary>
    [Fact]
    public async Task Endpoints_OtherServer_Forbidden()
    {
        await using var manager = StatusWatcherTests.WithAdmin();
        var anna = await AnnaAsync(manager);
        using var api = Client(manager, await TokenAsync(manager, anna.UserId));

        var listed = JsonDocument.Parse(await api.GetStringAsync(new Uri("/api/servers", UriKind.Relative))).RootElement;
        Assert.Equal([anna.Raid], listed.EnumerateArray().Select(server => server.GetProperty("id").GetString()));
        Assert.Equal(HttpStatusCode.NoContent, await PostAsync(api, $"/api/servers/{anna.Raid}/stop"));
        Assert.Equal("exited", manager.Docker.Containers.Values.Single(container => container.ServerId == anna.Raid).State);
        Assert.Equal(HttpStatusCode.NoContent, await PostAsync(api, $"/api/servers/{anna.Raid}/start"));
        Assert.Equal(HttpStatusCode.Conflict, await PostAsync(api, $"/api/servers/{anna.Raid}/update")); // no offer in the catalog
        Assert.Equal(HttpStatusCode.Forbidden, await PostAsync(api, $"/api/servers/{anna.Lobby}/stop"));
        using var anonymous = Client(manager, null);
        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(anonymous, "/api/servers"));
        using var wrong = Client(manager, "ovs_falsch");
        Assert.Equal(HttpStatusCode.Unauthorized, await PostAsync(wrong, $"/api/servers/{anna.Raid}/stop"));
    }

    /// <summary>AC3: a call writes a line naming the token, next to the lifecycle's own line.</summary>
    [Fact]
    public async Task Call_WithToken_AuditNamesToken()
    {
        await using var manager = StatusWatcherTests.WithAdmin();
        var anna = await AnnaAsync(manager);
        using var api = Client(manager, await TokenAsync(manager, anna.UserId));

        Assert.Equal(HttpStatusCode.NoContent, await PostAsync(api, $"/api/servers/{anna.Raid}/restart"));

        await using var scope = manager.Services.CreateAsyncScope();
        var lines = await scope.ServiceProvider.GetRequiredService<AuditLog>().QueryAsync(new AuditFilter(ServerId: anna.Raid), servers: null);
        var call = Assert.Single(lines, line => line.Action == AuditActions.ApiCalled);
        Assert.Equal(("anna", "skript", $"POST /api/servers/{anna.Raid}/restart 204"), (call.UserName, call.Target, call.Detail));
        Assert.Contains(lines, line => line.Action == AuditActions.ServerRestarted && line.UserName == "anna");
    }

    /// <summary>AC4: the OpenAPI document names every path, without a token.</summary>
    [Fact]
    public async Task OpenApi_Get_ListsEveryPath()
    {
        await using var manager = StatusWatcherTests.WithAdmin();
        manager.CreateClient().Dispose();
        using var anonymous = Client(manager, null);

        var document = JsonDocument.Parse(await anonymous.GetStringAsync(new Uri("/api/openapi.json", UriKind.Relative))).RootElement;

        Assert.Equal(["/api/servers", "/api/servers/{id}/start", "/api/servers/{id}/stop", "/api/servers/{id}/restart", "/api/servers/{id}/update"],
            document.GetProperty("paths").EnumerateObject().Select(path => path.Name));
    }

    [GeneratedRegex("ovs_[A-Za-z0-9_-]{20,}")]
    private static partial Regex Secret();

    /// <summary>Anna's id and the ids of her server and the other one.</summary>
    private sealed record AnnaWithServers(string UserId, string Raid, string Lobby);
}
