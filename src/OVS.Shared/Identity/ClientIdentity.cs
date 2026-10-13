using System.Security.Cryptography;

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

    /// <summary>The public key as SubjectPublicKeyInfo, sent in the hello.</summary>
    public byte[] PublicKey { get; }
    /// <summary>The lowercase hex SHA-256 of the public key: the user id on every server.</summary>
    public string Fingerprint { get; }

    /// <summary>A new random P-256 key pair.</summary>
    public static ClientIdentity Create() => new(ECDsa.Create(ECCurve.NamedCurves.nistP256));

    /// <summary>The key pair stored as PKCS#8.</summary>
    public static ClientIdentity FromPkcs8(byte[] pkcs8)
    {
        var ecDsa = ECDsa.Create();
        ecDsa.ImportPkcs8PrivateKey(pkcs8, out _);
        return new ClientIdentity(ecDsa);
    }

    /// <summary>The private key as PKCS#8, for the identity file.</summary>
    public byte[] ExportPkcs8() => key.ExportPkcs8PrivateKey();

    /// <summary>Signs data with SHA-256 (the handshake proof).</summary>
    public byte[] Sign(byte[] data) => key.SignData(data, HashAlgorithmName.SHA256);

    /// <summary>True when signature is publicKey's SHA-256 signature of data; false for any malformed key or signature.</summary>
    public static bool Verify(byte[] publicKey, byte[] data, byte[] signature)
    {
        try
        {
            using var ecDsa = ECDsa.Create();
            ecDsa.ImportSubjectPublicKeyInfo(publicKey, out _);
            return ecDsa.VerifyData(data, signature, HashAlgorithmName.SHA256);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>The fingerprint of a SubjectPublicKeyInfo: its SHA-256 as lowercase hex.</summary>
    public static string ComputeFingerprint(byte[] subjectPublicKeyInfo) =>
        Convert.ToHexStringLower(SHA256.HashData(subjectPublicKeyInfo));

    /// <summary>What the client signs: the server's nonce bound to the server certificate it actually sees.</summary>
    public static byte[] ProofData(byte[] nonce, byte[] serverCertHash) => [.. nonce, .. serverCertHash];

    /// <summary>Releases the key.</summary>
    public void Dispose() => key.Dispose();
}
