using System.Buffers.Binary;
using OVS.Server.Data;
using OVS.Shared.Identity;
using OVS.Shared.Protocol;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server;

/// <summary>Package 30: server logo upload, distribution and storage.</summary>
public sealed class ServerIconTests
{
    /// <summary>Just enough PNG for the server's header check: signature, IHDR with the size, padding.</summary>
    static byte[] Png(int width, int height, int size = 2048)
    {
        var png = new byte[size];
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(png, 0);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(8), 13);
        "IHDR"u8.CopyTo(png.AsSpan(12));
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20), height);
        Random.Shared.NextBytes(png.AsSpan(33));
        return png;
    }

    static async Task<(TestServer Server, TestClient Admin, TestClient Guest)> StartAsync()
    {
        var adminId = ClientIdentity.Create();
        var server = await TestServer.StartAsync(TestServer.Grant(adminId, "Admin"));
        var admin = await TestClient.ConnectAsync(server, "chef", adminId);
        var guest = await TestClient.ConnectAsync(server, "gast");
        return (server, admin, guest);
    }

    [Fact]
    public async Task Set_ValidPng_StoredAndBroadcast()
    {
        var (server, admin, guest) = await StartAsync();
        await using var _ = server;
        await using var a = admin;
        await using var g = guest;
        var png = Png(256, 256);

        await admin.SendAsync(new SetServerIcon(Convert.ToBase64String(png)) { RequestId = "r1" });
        var changed = await guest.WaitForAsync<ServerSettingsChanged>(m => m.Settings.IconHash is not null);
        Assert.Equal(ServerIconFormat.Hash(png), changed.Settings.IconHash);

        await guest.SendAsync(new GetServerIcon { RequestId = "r2" });
        var icon = await guest.WaitForAsync<ServerIcon>();
        Assert.Equal(("r2", changed.Settings.IconHash), (icon.RequestId, icon.Hash));
        Assert.Equal(png, Convert.FromBase64String(icon.PngBase64!));
        Assert.Contains(server.Log, l => l.Contains("Server-Logo geändert von chef"));
    }

    public static TheoryData<string, string, string> Invalid => new()
    {
        // what, expected code, expected detail part
        { "zu gross", Codes.InvalidValue, "zu gross" },
        { "kein png", Codes.InvalidValue, "kein PNG" },
        { "nicht quadratisch", Codes.InvalidValue, "quadratisch" },
        { "zu klein", Codes.InvalidValue, "Pixel" },
        { "kein base64", Codes.InvalidValue, "Base64" },
        { "ohne recht", Codes.PermissionDenied, "" },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public async Task Set_Invalid_Rejected(string what, string code, string detail)
    {
        var (server, admin, guest) = await StartAsync();
        await using var _ = server;
        await using var a = admin;
        await using var g = guest;
        var payload = what switch
        {
            "zu gross" => Convert.ToBase64String(Png(256, 256, ServerIconFormat.MaxBytes + 1)),
            "kein png" => Convert.ToBase64String(new byte[2048]),
            "nicht quadratisch" => Convert.ToBase64String(Png(300, 200)),
            "zu klein" => Convert.ToBase64String(Png(32, 32)),
            "kein base64" => "!!!",
            _ => Convert.ToBase64String(Png(256, 256)),
        };
        var sender = what == "ohne recht" ? guest : admin;

        await sender.SendAsync(new SetServerIcon(payload) { RequestId = "r1" });
        var error = await sender.ErrorAsync("r1");
        Assert.Equal(code, error.Code);
        Assert.Contains(detail, error.Detail ?? "");
        Assert.False(File.Exists(Path.Combine(server.DataDir, ServerIconStore.FileName)));
    }

    [Fact]
    public async Task Icon_SurvivesRestart_AndCanBeRemoved()
    {
        var adminId = ClientIdentity.Create();
        var server = await TestServer.StartAsync(TestServer.Grant(adminId, "Admin"));
        var png = Png(128, 128);
        await using (var admin = await TestClient.ConnectAsync(server, "chef", adminId))
        {
            await admin.SendAsync(new SetServerIcon(Convert.ToBase64String(png)));
            await admin.WaitForAsync<ServerSettingsChanged>(m => m.Settings.IconHash is not null);
        }

        server = await server.RestartAsync();
        await using var _ = server;
        await using var again = await TestClient.ConnectAsync(server, "chef", adminId);
        Assert.Equal(ServerIconFormat.Hash(png), again.Welcome.Snapshot.Settings.IconHash);

        await again.SendAsync(new SetServerIcon(null));
        await again.WaitForAsync<ServerSettingsChanged>(m => m.Settings.IconHash is null);
        Assert.False(File.Exists(Path.Combine(server.DataDir, ServerIconStore.FileName)));
        Assert.Contains(server.Log, l => l.Contains("Server-Logo entfernt von chef"));
    }
}
