using System.Globalization;
using OVS.Client.Localization;
using Avalonia;
using Avalonia.Media.Imaging;
using OVS.Shared.Protocol;

namespace OVS.Client.Services;

/// <summary>
/// Turns a chosen image file into the server logo (A21): at most 3 MB, PNG or JPG, exactly square,
/// at least 64 px; larger images are scaled down to 256 x 256 PNG. Everything is checked before anything is sent.
/// </summary>
internal static class IconImport
{
    /// <summary>The largest image file accepted.</summary>
    public const long MaxFileBytes = 3 * 1024 * 1024;
    /// <summary>The side length the logo is scaled to.</summary>
    public const int TargetSize = 256;

    /// <summary>Reads an image and makes the square PNG to upload, or says why not.</summary>
    public static IconImportResult Prepare(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists) return new IconImportResult(null, Strings.Icon_Missing);
        if (file.Length > MaxFileBytes) return new IconImportResult(null, Strings.Icon_TooBig);

        var head = new byte[8];
        using (var stream = file.OpenRead()) stream.ReadExactly(head, 0, (int)Math.Min(8, file.Length));
        bool isPng = head.AsSpan().SequenceEqual((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        bool isJpg = head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF;
        if (!isPng && !isJpg) return new IconImportResult(null, Strings.Icon_Format);

        Bitmap image;
        try
        {
            using var stream = file.OpenRead();
            #pragma warning disable CA2000 // disposed by the using block below
            image = new Bitmap(stream);
            #pragma warning restore CA2000
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new IconImportResult(null, Strings.Icon_Unreadable);
        }

        using (image)
        {
            var (width, height) = (image.PixelSize.Width, image.PixelSize.Height);
            if (width != height) return new IconImportResult(null, TextFormat.Of(Strings.Icon_NotSquare, width, height));
            if (width < ServerIconFormat.MinSize) return new IconImportResult(null, TextFormat.Of(Strings.Icon_TooSmall, ServerIconFormat.MinSize));

            using var output = new MemoryStream();
            if (width > TargetSize)
            {
                using var scaled = image.CreateScaledBitmap(new PixelSize(TargetSize, TargetSize), BitmapInterpolationMode.HighQuality);
                scaled.Save(output);
            }
            else
            {
                image.Save(output);
            }
            var png = output.ToArray();
            return ServerIconFormat.Validate(png) is not null ? new IconImportResult(null, Strings.Icon_Unreadable) : new IconImportResult(png, null);
        }
    }
}
