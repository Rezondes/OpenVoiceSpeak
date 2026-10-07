using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace OVS.Shared.Identity;

/// <summary>The SHA-256 fingerprint of a certificate: as bytes for the handshake proof, as lowercase hex for display and trust.</summary>
public static class CertFingerprint
{
    public static byte[] Compute(X509Certificate cert) => SHA256.HashData(cert.GetRawCertData());

    public static string ComputeHex(X509Certificate cert) => Convert.ToHexStringLower(Compute(cert));
}
