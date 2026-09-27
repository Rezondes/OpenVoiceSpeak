using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace OVS.Shared.Identity;

/// <summary>A user's key pair. The fingerprint of the public key is the user id on every server.</summary>
public sealed class ClientIdentity : IDisposable
{
    readonly ECDsa key;

    ClientIdentity(ECDsa key)
    {
        this.key = key;
        PublicKey = key.ExportSubjectPublicKeyInfo();
        Fingerprint = ComputeFingerprint(PublicKey);
    }

    public byte[] PublicKey { get; }
    public string Fingerprint { get; }

    public static ClientIdentity Create() => new(ECDsa.Create(ECCurve.NamedCurves.nistP256));

    public static ClientIdentity FromPkcs8(byte[] pkcs8)
    {
        var k = ECDsa.Create();
        k.ImportPkcs8PrivateKey(pkcs8, out _);
        return new ClientIdentity(k);
    }

    public byte[] ExportPkcs8() => key.ExportPkcs8PrivateKey();

    public byte[] Sign(byte[] data) => key.SignData(data, HashAlgorithmName.SHA256);

    public static bool Verify(byte[] publicKey, byte[] data, byte[] signature)
    {
        try
        {
            using var k = ECDsa.Create();
            k.ImportSubjectPublicKeyInfo(publicKey, out _);
            return k.VerifyData(data, signature, HashAlgorithmName.SHA256);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    public static string ComputeFingerprint(byte[] subjectPublicKeyInfo) =>
        Convert.ToHexStringLower(SHA256.HashData(subjectPublicKeyInfo));

    /// <summary>What the client signs: the server's nonce bound to the server certificate it actually sees.</summary>
    public static byte[] ProofData(byte[] nonce, byte[] serverCertHash) => [.. nonce, .. serverCertHash];

    public void Dispose() => key.Dispose();
}

public static class CertFingerprint
{
    public static byte[] Hash(X509Certificate cert) => SHA256.HashData(cert.GetRawCertData());

    public static string Of(X509Certificate cert) => Convert.ToHexStringLower(Hash(cert));
}
