using System.IO.Compression;
using System.Security.Cryptography;

namespace OVS.Manager.Data;

/// <summary>
/// Package M33 (AC1, AC3): the sealed form of a manager backup: a zip, encrypted with AES-256-GCM under a key that PBKDF2
/// (SHA-256, 600 000 rounds) derives from the admin's password. A wrong password and a damaged file look the same: the tag
/// does not match.
/// </summary>
internal static class ManagerArchive
{
    /// <summary>The file extension of a manager backup.</summary>
    public const string Extension = ".ovsmb";

    const int Iterations = 600_000;
    const int SaltBytes = 16;
    const int NonceBytes = 12;
    const int TagBytes = 16;
    static readonly byte[] Magic = "OVSMB1"u8.ToArray();
    static int HeaderBytes => Magic.Length + SaltBytes + NonceBytes + TagBytes;

    /// <summary>The entries zipped and encrypted with <paramref name="password"/>.</summary>
    public static byte[] Seal(IReadOnlyDictionary<string, byte[]> entries, string password)
    {
        using var zipped = new MemoryStream();
        using (var zip = new ZipArchive(zipped, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, bytes) in entries)
            {
                using var stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
                stream.Write(bytes);
            }
        var plain = zipped.ToArray();
        var archive = new byte[HeaderBytes + plain.Length];
        Magic.CopyTo(archive, 0);
        var salt = archive.AsSpan(Magic.Length, SaltBytes);
        var nonce = archive.AsSpan(Magic.Length + SaltBytes, NonceBytes);
        RandomNumberGenerator.Fill(salt);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(KeyOf(password, salt), TagBytes);
        aes.Encrypt(nonce, plain, archive.AsSpan(HeaderBytes), archive.AsSpan(Magic.Length + SaltBytes + NonceBytes, TagBytes), Magic);
        return archive;
    }

    /// <summary>The entries of <paramref name="archive"/>, opened with <paramref name="password"/>.</summary>
    /// <exception cref="InvalidDataException">It is no manager backup, the password is wrong or the file is damaged.</exception>
    public static Dictionary<string, byte[]> Open(byte[] archive, string password)
    {
        ArgumentNullException.ThrowIfNull(archive);
        if (archive.Length < HeaderBytes || !archive.AsSpan(0, Magic.Length).SequenceEqual(Magic)) throw new InvalidDataException("no manager backup");
        var plain = new byte[archive.Length - HeaderBytes];
        try
        {
            using var aes = new AesGcm(KeyOf(password, archive.AsSpan(Magic.Length, SaltBytes)), TagBytes);
            aes.Decrypt(archive.AsSpan(Magic.Length + SaltBytes, NonceBytes), archive.AsSpan(HeaderBytes),
                archive.AsSpan(Magic.Length + SaltBytes + NonceBytes, TagBytes), plain, Magic);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidDataException("wrong password or damaged archive", exception);
        }
        // ponytail: the whole archive in memory; the manager's database and keys are a few MB
        using var zip = new ZipArchive(new MemoryStream(plain), ZipArchiveMode.Read);
        var entries = new Dictionary<string, byte[]>();
        foreach (var entry in zip.Entries)
        {
            using var stream = entry.Open();
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            entries[entry.FullName] = copy.ToArray();
        }
        return entries;
    }

    static byte[] KeyOf(string password, ReadOnlySpan<byte> salt) => Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
}
