using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace OVS.Shared.Identity;

public static class CertFingerprint
{
    public static byte[] Hash(X509Certificate cert) => SHA256.HashData(cert.GetRawCertData());

    public static string Of(X509Certificate cert) => Convert.ToHexStringLower(Hash(cert));
}
