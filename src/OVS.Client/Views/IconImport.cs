using OVS.Client.Localization;
using Avalonia;
using Avalonia.Media.Imaging;
using OVS.Shared.Protocol;

namespace OVS.Client.Views;

/// <summary>
/// Turns a chosen image file into the server logo (A21): at most 3 MB, PNG or JPG, exactly square,
/// at least 64 px; larger images are scaled down to 256 x 256 PNG. Everything is checked before anything is sent.
/// </summary>
public static class IconImport
{
    public const long MaxFileBytes = 3 * 1024 * 1024;
    public const int TargetSize = 256;

    public static (byte[]? Png, string? Error) Prepare(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists) return (null, Strings.Icon_Missing);
        if (file.Length > MaxFileBytes) return (null, Strings.Icon_TooBig);

        var head = new byte[8];
        using (var stream = file.OpenRead()) stream.ReadExactly(head, 0, (int)Math.Min(8, file.Length));
        bool isPng = head.AsSpan().SequenceEqual((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        bool isJpg = head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF;
        if (!isPng && !isJpg) return (null, Strings.Icon_Format);

        Bitmap image;
        try
        {
            using var stream = file.OpenRead();
            image = new Bitmap(stream);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return (null, Strings.Icon_Unreadable);
        }

        using (image)
        {
            var (width, height) = (image.PixelSize.Width, image.PixelSize.Height);
            if (width != height) return (null, string.Format(Strings.Icon_NotSquare, width, height));
            if (width < ServerIconFormat.MinSize) return (null, string.Format(Strings.Icon_TooSmall, ServerIconFormat.MinSize));

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
            return ServerIconFormat.Validate(png) is { } problem ? (null, problem) : (png, null);
        }
    }
}
