using OVS.Shared.Identity;

namespace OVS.Tests.Shared;

public class ClientIdentityTests
{
    [Fact]
    public void SaveLoad_KeepsFingerprint()
    {
        using var id = ClientIdentity.Create();
        using var loaded = ClientIdentity.FromPkcs8(id.ExportPkcs8());
        Assert.Equal(id.Fingerprint, loaded.Fingerprint);
        Assert.Equal(64, id.Fingerprint.Length);
    }

    [Fact]
    public void Verify_OwnSignature_True()
    {
        using var id = ClientIdentity.Create();
        byte[] data = [1, 2, 3];
        Assert.True(ClientIdentity.Verify(id.PublicKey, data, id.Sign(data)));
    }

    [Fact]
    public void Verify_TamperedData_False()
    {
        using var id = ClientIdentity.Create();
        var sig = id.Sign([1, 2, 3]);
        Assert.False(ClientIdentity.Verify(id.PublicKey, [1, 2, 4], sig));
    }

    [Fact]
    public void Verify_GarbageKey_False()
    {
        Assert.False(ClientIdentity.Verify([1, 2, 3], [1], [1]));
    }
}
