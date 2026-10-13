using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using OVS.Shared.IO;

namespace OVS.Server.Tls;

public static class ServerCertificate
{
    public const string FileName = "cert.pfx";

    /// <summary>Self-signed certificate, created once and kept in the data dir so clients' TOFU pins stay valid.</summary>
    public static X509Certificate2 LoadOrCreate(string dataDir)
    {
        var path = Path.Combine(dataDir, FileName);
        if (!File.Exists(path))
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest("CN=OpenVoiceSpeak", key, HashAlgorithmName.SHA256);
            using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(20)); // clock: real time, clients check the validity against their real clocks
            SafeFile.WriteAtomic(path, cert.Export(X509ContentType.Pfx), flushToDisk: false);
        }
        // Reloading from PFX matters on Windows: SslStream cannot use the ephemeral key of CreateSelfSigned.
        return X509CertificateLoader.LoadPkcs12FromFile(path, null);
    }
}
