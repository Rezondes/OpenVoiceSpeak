using System.Security.Cryptography;
using OVS.Server.Data.Records;
using OVS.Server.Security;
using OVS.Shared.Identity;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages.ServerBackups;
using OVS.Shared.Protocol.Messages.ServerInfo;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server;

/// <summary>Padded base64 (one and two '=') gives the same decisions at every place the server decodes base64 input.</summary>
public class Base64InputTests
{
    static int Padding(string base64) => base64.Length - base64.TrimEnd('=').Length;

    static async Task<AdminRig> StartWithAdminAsync()
    {
        var admin = ClientIdentity.Create();
        var server = await TestServer.StartAsync(TestServer.Grant(admin, "Admin"));
        return new AdminRig(server, await TestClient.ConnectAsync(server, "chef", admin));
    }

    [Fact]
    public async Task UploadChunk_Padded_SameLimit()
    {
        var (server, admin) = await StartWithAdminAsync();
        await using var _ = server;
        await using var client = admin;

        // exactly BackupChunkBytes (one '=') and one byte less (two '=') pass
        foreach (var (size, padding) in new[] { (ProtocolLimits.BackupChunkBytes, 1), (ProtocolLimits.BackupChunkBytes - 1, 2) })
        {
            var text = Convert.ToBase64String(new byte[size]);
            Assert.Equal(padding, Padding(text));
            var id = Guid.NewGuid().ToString("N");
            await admin.SendAsync(new UploadBackupChunk(id, 0, text, false) { RequestId = "ok" + size });
            Assert.Equal(size, (await admin.WaitForAsync<UploadBackupAck>(uploadBackupAck => uploadBackupAck.RequestId == "ok" + size)).Received);
        }

        // one byte more is refused
        var tooLarge = Guid.NewGuid().ToString("N");
        await admin.SendAsync(new UploadBackupChunk(tooLarge, 0, Convert.ToBase64String(new byte[ProtocolLimits.BackupChunkBytes + 1]), false) { RequestId = "gross" });
        Assert.Equal(ReasonCodes.InvalidValue, (await admin.ErrorAsync("gross")).Code);
    }

    [Fact]
    public async Task ServerIcon_Padded_Validates()
    {
        var (server, admin) = await StartWithAdminAsync();
        await using var _ = server;
        await using var client = admin;

        // the largest allowed logo (one '=') and one byte less (two '='); only the header is checked, the rest is padding
        foreach (var (size, padding) in new[] { (ServerIconFormat.MaxBytes, 1), (ServerIconFormat.MaxBytes - 1, 2) })
        {
            var png = new byte[size];
            TestImages.Encode(64, 64).CopyTo(png, 0);
            var text = Convert.ToBase64String(png);
            Assert.Equal(padding, Padding(text));
            await admin.SendAsync(new SetServerIcon(text) { RequestId = "logo" + size });
            await admin.WaitForAsync<ServerSettingsChanged>(serverSettingsChanged => serverSettingsChanged.Settings.IconHash == ServerIconFormat.ComputeHash(png));
        }
    }

    [Fact]
    public void StoredHash_Padded_StillValid()
    {
        // a PBKDF2 hash as the server stores it: a 16-byte salt (two '=') and a 32-byte key (one '=')
        var salt = Enumerable.Range(1, 16).Select(index => (byte)index).ToArray();
        var key = Rfc2898DeriveBytes.Pbkdf2("geheim"u8, salt, StoredPassword.HashIterations, HashAlgorithmName.SHA256, 32);
        var (saltText, keyText) = (Convert.ToBase64String(salt), Convert.ToBase64String(key));
        Assert.Equal((2, 1), (Padding(saltText), Padding(keyText)));
        var stored = $"pbkdf2${StoredPassword.HashIterations}${saltText}${keyText}";

        Assert.True(StoredPassword.FromStored(stored).IsValid());
        Assert.True(new ServerSettings { StoredPassword = StoredPassword.FromStored(stored) }.CheckPassword("geheim"));
        Assert.False(new ServerSettings { StoredPassword = StoredPassword.FromStored(stored) }.CheckPassword("Geheim"));
        Assert.True(StoredPassword.Create("geheim")!.IsValid());

        // the unsalted SHA-256 hex from before Package 91
        var legacy = Convert.ToHexStringLower(SHA256.HashData("geheim"u8));
        Assert.True(StoredPassword.FromStored(legacy).IsValid());
        Assert.True(new ServerSettings { StoredPassword = StoredPassword.FromStored(legacy) }.CheckPassword("geheim"));

        // a salt or key one byte short is still refused
        var shortSalt = Convert.ToBase64String(salt[..15]);
        var shortKey = Convert.ToBase64String(key[..31]);
        Assert.False(StoredPassword.FromStored($"pbkdf2${StoredPassword.HashIterations}${shortSalt}${keyText}").IsValid());
        Assert.False(StoredPassword.FromStored($"pbkdf2${StoredPassword.HashIterations}${saltText}${shortKey}").IsValid());
    }

    [Fact]
    public async Task HandshakeKey_Padded_SameBytes()
    {
        await using var server = await TestServer.StartAsync();
        var identity = ClientIdentity.Create();
        Assert.Equal(2, Padding(Convert.ToBase64String(identity.PublicKey)));
        await using var client = await TestClient.ConnectAsync(server, "anna", identity);

        // the server derives the fingerprint from the decoded key, so the same bytes give the same fingerprint
        var self = Assert.Single(client.Welcome.Snapshot.Users, user => user.SessionId == client.Id);
        Assert.Equal(identity.Fingerprint, self.Fingerprint);
    }
}
